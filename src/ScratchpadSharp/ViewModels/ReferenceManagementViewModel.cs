using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ReactiveUI;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.PackageManagement;

namespace ScratchpadSharp.ViewModels;

public class ReferenceManagementViewModel : ReactiveObject
{
    private readonly string tabId;

    private readonly ProjectContext projectContext;

    private string localSearchQuery = string.Empty;
    private string onlineSearchQuery = string.Empty;
    private bool isSearching;
    private bool includePreRelease;
    private IPackageSearchMetadata? selectedPackage;
    private string? selectedVersion;
    private readonly ObservableCollection<string> availableVersions = new();
    private bool isInstalling;
    private string? selectedSource;

    public ObservableCollection<AssemblyReferenceItem> AssemblyReferences { get; } = new();
    public ObservableCollection<IPackageSearchMetadata> LocalPackages { get; } = new();
    public ObservableCollection<IPackageSearchMetadata> OnlinePackages { get; } = new();
    public ObservableCollection<string> PackageSources { get; } = new();

    // In-memory cache for local packages filtering
    private List<IPackageSearchMetadata> allLocalPackages = new();

    // Status
    private string statusText = string.Empty;

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    private bool showRestoreButton;

    public bool ShowRestoreButton
    {
        get => showRestoreButton;
        set => this.RaiseAndSetIfChanged(ref showRestoreButton, value);
    }

    public string LocalSearchQuery
    {
        get => localSearchQuery;
        set => this.RaiseAndSetIfChanged(ref localSearchQuery, value);
    }

    public string OnlineSearchQuery
    {
        get => onlineSearchQuery;
        set => this.RaiseAndSetIfChanged(ref onlineSearchQuery, value);
    }

    public bool IncludePreRelease
    {
        get => includePreRelease;
        set => this.RaiseAndSetIfChanged(ref includePreRelease, value);
    }

    public bool IsSearching
    {
        get => isSearching;
        set => this.RaiseAndSetIfChanged(ref isSearching, value);
    }

    public string? SelectedSource
    {
        get => selectedSource;
        set => this.RaiseAndSetIfChanged(ref selectedSource, value);
    }

    public IPackageSearchMetadata? SelectedPackage
    {
        get => selectedPackage;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedPackage, value);
            _ = LoadVersionsAsync(value);
        }
    }

    public string? SelectedVersion
    {
        get => selectedVersion;
        set => this.RaiseAndSetIfChanged(ref selectedVersion, value);
    }

    public ObservableCollection<string> AvailableVersions => availableVersions;

    public bool IsInstalling
    {
        get => isInstalling;
        set => this.RaiseAndSetIfChanged(ref isInstalling, value);
    }

    public ReactiveCommand<Unit, Unit> LocalSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> OnlineSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallPackageCommand { get; }
    public ReactiveCommand<AssemblyReferenceItem, Unit> RemoveAssemblyReferenceCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Uri?, Unit> OpenUrlCommand { get; }

    public ReferenceManagementViewModel(string tabId, ProjectContext projectContext)
    {
        this.tabId = tabId;
        this.projectContext = projectContext;

        RefreshReferences();

        // Setup Commands
        LocalSearchCommand = ReactiveCommand.Create(FilterLocalPackages);
        OnlineSearchCommand = ReactiveCommand.CreateFromTask(SearchOnlineAsync);

        OpenUrlCommand = ReactiveCommand.Create<Uri?>(url =>
        {
            if (url == null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url.ToString(),
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        });

        InstallPackageCommand = ReactiveCommand.CreateFromTask(InstallPackageAsync,
            this.WhenAnyValue(x => x.SelectedPackage, x => x.SelectedVersion,
                (p, v) => p != null && !string.IsNullOrEmpty(v)));

        // RemovePackageCommand = ReactiveCommand.CreateFromTask(RemovePackageAsync);

        RemoveAssemblyReferenceCommand = ReactiveCommand.CreateFromTask<AssemblyReferenceItem>(async req =>
        {
            if (req != null)
            {
                AssemblyReferences.Remove(req);
                if (req.Source == "Local File")
                {
                    await ProjectService.Instance.RemoveReferenceAsync(this.tabId, this.projectContext, req.Path);
                }
            }
        });

        CloseCommand = ReactiveCommand.Create(() => { });

        // Handle errors from async commands to prevent app crash
        InstallPackageCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => StatusText = $"Install failed: {ex.Message}");

        RemoveAssemblyReferenceCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => StatusText = $"Remove failed: {ex.Message}");

        // Initial Load
        _ = LoadDataAsync();

        // Subscribe to search query changes with debounce
        this.WhenAnyValue(x => x.LocalSearchQuery)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => FilterLocalPackages());

        this.WhenAnyValue(x => x.OnlineSearchQuery)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Select(_ => Unit.Default)
            .InvokeCommand(OnlineSearchCommand);

        this.WhenAnyValue(x => x.IncludePreRelease)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Select(_ => Unit.Default)
            .InvokeCommand(OnlineSearchCommand);
    }

    private async Task LoadDataAsync()
    {
        await LoadPackageSourcesAsync();
        await LoadLocalPackagesAsync();
        // CheckRestoreNeeded();
    }

    private void RefreshReferences()
    {
        AssemblyReferences.Clear();

        foreach (var refPath in projectContext.Config.References)
        {
            // Skip BCL assembly names (e.g. "System.Runtime") — only show actual file paths
            if (!refPath.Contains(Path.DirectorySeparatorChar) &&
                !refPath.Contains('/') &&
                !refPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            AssemblyReferences.Add(new AssemblyReferenceItem
                { Name = Path.GetFileName(refPath), Path = refPath, IsDefault = false, Source = "Local File" });
        }

        foreach (var pkg in projectContext.Config.NuGetPackages)
        {
            AssemblyReferences.Add(new AssemblyReferenceItem
                { Name = pkg.Key, Path = pkg.Value, IsDefault = false, Source = "NuGet" });
        }
    }

    // private void CheckRestoreNeeded()
    // {
    //     bool needed = false;
    //     foreach (var pkg in _config.NuGetPackages)
    //     {
    //         if (!_allLocalPackages.Any(p =>
    //                 p.Id.Equals(pkg.Key, StringComparison.OrdinalIgnoreCase) && p.Version == pkg.Value))
    //         {
    //             needed = true;
    //             break;
    //         }
    //     }
    //
    //     ShowRestoreButton = needed;
    // }


    private async Task LoadPackageSourcesAsync()
    {
        try
        {
            var sources = await NuGetService.Instance.GetPackageSourcesAsync();
            PackageSources.Clear();
            PackageSources.Add("All Sources");
            foreach (var s in sources) PackageSources.Add(s);
            SelectedSource = PackageSources.First();
        }
        catch
        {
            PackageSources.Add("All Sources");
            PackageSources.Add("nuget.org");
            SelectedSource = PackageSources.First();
        }
    }

    private async Task LoadLocalPackagesAsync()
    {
        try
        {
            var packages = await NuGetService.Instance.GetLocalPackagesAsync();
            allLocalPackages = packages.ToList();
            FilterLocalPackages();
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading local packages: {ex.Message}";
        }
    }

    private void FilterLocalPackages()
    {
        LocalPackages.Clear();
        if (string.IsNullOrWhiteSpace(LocalSearchQuery))
        {
            foreach (var p in allLocalPackages) LocalPackages.Add(p);
        }
        else
        {
            var filtered =
                allLocalPackages.Where(p => p.Identity.Id.Contains(LocalSearchQuery, StringComparison.OrdinalIgnoreCase));
            foreach (var p in filtered) LocalPackages.Add(p);
        }
    }

    private async Task SearchOnlineAsync()
    {
        if (string.IsNullOrWhiteSpace(OnlineSearchQuery)) return;

        IsSearching = true;
        StatusText = "Searching...";
        OnlinePackages.Clear();

        try
        {
            string? source = SelectedSource == "All Sources" ? null : SelectedSource;
            var results = await NuGetService.Instance.SearchAsync(OnlineSearchQuery, IncludePreRelease, source);
            var packageSearchMetadatas = results as IPackageSearchMetadata[] ?? results.ToArray();
            foreach (var r in packageSearchMetadatas) OnlinePackages.Add(r);
            StatusText = $"Found {packageSearchMetadatas.Count()} packages";
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task LoadVersionsAsync(IPackageSearchMetadata? package)
    {
        availableVersions.Clear();
        SelectedVersion = null;

        if (package == null) return;

        var versions = await NuGetService.Instance.GetPackageVersionsAsync(package.Identity.Id);
        foreach (var v in versions) availableVersions.Add(v);

        if (availableVersions.Any())
        {
            SelectedVersion = availableVersions.First();
        }
    }

    private async Task InstallPackageAsync()
    {
        if (SelectedPackage == null || SelectedVersion == null) return;

        var identity = new PackageIdentity(SelectedPackage.Identity.Id, NuGetVersion.Parse(SelectedVersion));
        StatusText = $"Installing {identity.Id} {identity.Version}...";
        IsInstalling = true;
        try
        {
            await ProjectService.Instance.AddPackageAsync(tabId, projectContext, identity);
            StatusText = $"Installed {identity.Id} {identity.Version}";
            RefreshReferences();
        }
        catch (Exception ex)
        {
            StatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    public async Task AddReferenceFromFile(string filePath)
    {
        if (File.Exists(filePath) && AssemblyReferences.All(r => r.Path != filePath))
        {
            AssemblyReferences.Add(new AssemblyReferenceItem
            {
                Name = Path.GetFileName(filePath),
                Path = filePath,
                IsDefault = false,
                Source = "Local File"
            });

            await ProjectService.Instance.AddReferenceAsync(tabId, projectContext, filePath);
        }
    }
}

public class AssemblyReferenceItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string Source { get; set; } = string.Empty;
}