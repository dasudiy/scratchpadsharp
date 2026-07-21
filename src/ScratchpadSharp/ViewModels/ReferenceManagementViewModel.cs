using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ReactiveUI;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.Core.Configuration;
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
    private bool includePreReleaseVersions;
    private IPackageSearchMetadata? selectedPackage;
    private IPackageSearchMetadata? selectedPackageDetails;
    private PackageVersionItem? selectedVersionItem;
    private readonly ObservableCollection<PackageVersionItem> availableVersions = new();
    private bool isInstalling;
    private string? selectedSource;
    private double installProgress;

    public ObservableCollection<AssemblyReferenceItem> AssemblyReferences { get; } = new();
    public ObservableCollection<IPackageSearchMetadata> LocalPackages { get; } = new();
    public ObservableCollection<IPackageSearchMetadata> OnlinePackages { get; } = new();
    public ObservableCollection<string> PackageSources { get; } = new();

    private List<IPackageSearchMetadata> allLocalPackages = new();

    private string statusText = string.Empty;
    private decimal timeoutSeconds;
    private string connectionString = string.Empty;
    private string scriptSettingsStatus = string.Empty;

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public decimal TimeoutSeconds
    {
        get => timeoutSeconds;
        set
        {
            this.RaiseAndSetIfChanged(ref timeoutSeconds, value);
            UpdateInheritanceHints();
        }
    }

    public string ConnectionString
    {
        get => connectionString;
        set
        {
            this.RaiseAndSetIfChanged(ref connectionString, value);
            UpdateInheritanceHints();
        }
    }

    public string ScriptSettingsStatus
    {
        get => scriptSettingsStatus;
        set => this.RaiseAndSetIfChanged(ref scriptSettingsStatus, value);
    }

    public string TimeoutInheritanceHint { get; private set; } = string.Empty;
    public string ConnectionStringInheritanceHint { get; private set; } = string.Empty;

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

    public bool IncludePreReleaseVersions
    {
        get => includePreReleaseVersions;
        set => this.RaiseAndSetIfChanged(ref includePreReleaseVersions, value);
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
            _ = OnSelectedPackageChangedAsync(value);
        }
    }

    public IPackageSearchMetadata? SelectedPackageDetails
    {
        get => selectedPackageDetails;
        set => this.RaiseAndSetIfChanged(ref selectedPackageDetails, value);
    }

    public PackageVersionItem? SelectedVersionItem
    {
        get => selectedVersionItem;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedVersionItem, value);
            _ = LoadVersionDetailsAsync(value);
        }
    }

    public ObservableCollection<PackageVersionItem> AvailableVersions => availableVersions;

    public bool IsInstalling
    {
        get => isInstalling;
        set => this.RaiseAndSetIfChanged(ref isInstalling, value);
    }

    public double InstallProgress
    {
        get => installProgress;
        set => this.RaiseAndSetIfChanged(ref installProgress, value);
    }

    public ReactiveCommand<Unit, Unit> LocalSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> OnlineSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallPackageCommand { get; }
    public ReactiveCommand<AssemblyReferenceItem, Unit> RemoveAssemblyReferenceCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Uri?, Unit> OpenUrlCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyScriptSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetScriptSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> RestorePackagesCommand { get; }

    public ReferenceManagementViewModel(string tabId, ProjectContext projectContext)
    {
        this.tabId = tabId;
        this.projectContext = projectContext;

        TimeoutSeconds = projectContext.Config.TimeoutSeconds > 0
            ? projectContext.Config.TimeoutSeconds
            : ApplicationSettings.DefaultTimeoutSeconds;
        ConnectionString = projectContext.Config.ConnectionString ?? string.Empty;
        UpdateInheritanceHints();

        RefreshReferences();
        ShowRestoreButton = projectContext.Config.NuGetPackages.Count > 0;

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
            this.WhenAnyValue(x => x.SelectedPackage, x => x.SelectedVersionItem,
                (p, v) => p != null && v != null));

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
        ApplyScriptSettingsCommand = ReactiveCommand.Create(ApplyScriptSettings);
        ResetScriptSettingsCommand = ReactiveCommand.Create(ResetScriptSettingsToDefaults);
        RestorePackagesCommand = ReactiveCommand.CreateFromTask(RestorePackagesAsync,
            this.WhenAnyValue(x => x.IsInstalling, installing => !installing));

        InstallPackageCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => StatusText = $"Install failed: {ex.Message}");

        RemoveAssemblyReferenceCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => StatusText = $"Remove failed: {ex.Message}");

        RestorePackagesCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => StatusText = $"Restore failed: {ex.Message}");

        _ = LoadDataAsync();

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

        this.WhenAnyValue(x => x.IncludePreReleaseVersions)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(includePreRelease =>
            {
                _ = ReloadVersionsAsync();
            });
    }

    private async Task LoadDataAsync()
    {
        await LoadPackageSourcesAsync();
        await LoadLocalPackagesAsync();
    }

    private void ApplyScriptSettings()
    {
        projectContext.Config.TimeoutSeconds = (int)TimeoutSeconds;
        projectContext.Config.ConnectionString = ConnectionString ?? string.Empty;
        UpdateInheritanceHints();
        ScriptSettingsStatus = "Applied to in-memory config. Save the query to persist config.json.";
    }

    private void ResetScriptSettingsToDefaults()
    {
        var defaults = ConfigurationLoader.CreateDefaultConfig();
        TimeoutSeconds = defaults.TimeoutSeconds > 0
            ? defaults.TimeoutSeconds
            : ApplicationSettings.DefaultTimeoutSeconds;
        ConnectionString = defaults.ConnectionString ?? string.Empty;
        ApplyScriptSettings();
        ScriptSettingsStatus = "Reset to ScriptDefaults (still need Save on the query to persist).";
    }

    private async Task RestorePackagesAsync()
    {
        StatusText = "Restoring packages...";
        try
        {
            await ProjectService.Instance.ResolveConfiguredPackagesAsync(tabId, projectContext);
            RefreshReferences();
            ShowRestoreButton = projectContext.Config.NuGetPackages.Count > 0;
            StatusText = "Packages restored";
        }
        catch (Exception ex)
        {
            StatusText = $"Restore failed: {ex.Message}";
            ShowRestoreButton = true;
        }
    }

    private void UpdateInheritanceHints()
    {
        var defaultTimeout = ApplicationSettings.DefaultTimeoutSeconds;
        TimeoutInheritanceHint = (int)TimeoutSeconds == defaultTimeout
            ? $"Matches global default ({defaultTimeout}s) — inherited unless you change it."
            : $"Custom for this query (global default is {defaultTimeout}s).";

        var defaultCs = ConfigurationLoader.CreateDefaultConfig().ConnectionString ?? string.Empty;
        ConnectionStringInheritanceHint = string.IsNullOrWhiteSpace(ConnectionString)
            ? (string.IsNullOrEmpty(defaultCs)
                ? "Empty — no ScriptDefaults connection string to inherit."
                : $"Empty — at run time inherits ScriptDefaults ({defaultCs}).")
            : string.Equals(ConnectionString, defaultCs, StringComparison.Ordinal)
                ? "Matches ScriptDefaults."
                : "Set on this query (overrides ScriptDefaults).";

        this.RaisePropertyChanged(nameof(TimeoutInheritanceHint));
        this.RaisePropertyChanged(nameof(ConnectionStringInheritanceHint));
    }

    private void RefreshReferences()
    {
        AssemblyReferences.Clear();

        foreach (var refPath in projectContext.Config.References)
        {
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

    private async Task OnSelectedPackageChangedAsync(IPackageSearchMetadata? package)
    {
        availableVersions.Clear();
        SelectedVersionItem = null;
        SelectedPackageDetails = package;

        if (package == null) return;

        await LoadVersionsAsync(package);
    }

    private async Task ReloadVersionsAsync()
    {
        if (SelectedPackage == null) return;
        await LoadVersionsAsync(SelectedPackage);
    }

    private async Task LoadVersionsAsync(IPackageSearchMetadata package)
    {
        var previousVersion = SelectedVersionItem?.Version;
        availableVersions.Clear();
        SelectedVersionItem = null;

        var packageId = package.Identity.Id;
        var cachedVersions = (await NuGetService.Instance.GetCachedVersionsAsync(packageId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onlineVersions = await NuGetService.Instance.GetPackageVersionsAsync(packageId);

        var allVersions = onlineVersions
            .Concat(cachedVersions)
            .Select(v => NuGetVersion.Parse(v))
            .Distinct()
            .Where(v => IncludePreReleaseVersions || !v.IsPrerelease)
            .OrderByDescending(v => v)
            .ToList();

        foreach (var version in allVersions)
        {
            var versionString = version.ToNormalizedString();
            var isCached = cachedVersions.Contains(versionString) ||
                           NuGetService.Instance.IsPackageCached(new PackageIdentity(packageId, version));

            availableVersions.Add(new PackageVersionItem
            {
                Version = versionString,
                IsCached = isCached
            });
        }

        if (availableVersions.Count == 0) return;

        SelectedVersionItem = availableVersions.FirstOrDefault(v =>
                                  previousVersion != null &&
                                  v.Version.Equals(previousVersion, StringComparison.OrdinalIgnoreCase))
                              ?? availableVersions.First();
    }

    private async Task LoadVersionDetailsAsync(PackageVersionItem? versionItem)
    {
        if (SelectedPackage == null || versionItem == null)
        {
            SelectedPackageDetails = SelectedPackage;
            return;
        }

        try
        {
            var identity = new PackageIdentity(SelectedPackage.Identity.Id, NuGetVersion.Parse(versionItem.Version));
            var metadata = await NuGetService.Instance.GetPackageMetadataAsync(identity);
            SelectedPackageDetails = metadata ?? SelectedPackage;
        }
        catch
        {
            SelectedPackageDetails = SelectedPackage;
        }
    }

    private async Task InstallPackageAsync()
    {
        if (SelectedPackage == null || SelectedVersionItem == null) return;

        var identity = new PackageIdentity(SelectedPackage.Identity.Id,
            NuGetVersion.Parse(SelectedVersionItem.Version));

        var progress = new Progress<PackageInstallProgress>(p =>
        {
            StatusText = p.Message;
            InstallProgress = p.Percent;
        });

        StatusText = SelectedVersionItem.IsCached
            ? $"Installing {identity.Id} {identity.Version} from cache..."
            : $"Downloading and installing {identity.Id} {identity.Version}...";
        IsInstalling = true;
        InstallProgress = 0;

        try
        {
            await ProjectService.Instance.AddPackageAsync(tabId, projectContext, identity, default, progress);
            StatusText = $"Installed {identity.Id} {identity.Version}";
            InstallProgress = 100;
            RefreshReferences();
            ShowRestoreButton = projectContext.Config.NuGetPackages.Count > 0;
            await LoadLocalPackagesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Install failed: {ex.Message}";
            InstallProgress = 0;
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

public class PackageVersionItem
{
    public string Version { get; set; } = string.Empty;
    public bool IsCached { get; set; }
    public string DisplayText => IsCached ? $"{Version} (cached)" : $"{Version} (download)";
}
