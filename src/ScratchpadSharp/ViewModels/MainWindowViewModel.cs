using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private ScriptTabViewModel? selectedTab;
    private Window? mainWindow;

    private readonly IScriptExecutionService scriptService;

    public MainWindowViewModel(IScriptExecutionService scriptService)
    {
        this.scriptService = scriptService;
        Tabs = new ObservableCollection<ScriptTabViewModel>();

        NewTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.Create(CloseSelectedTab,
            this.WhenAnyValue(x => x.SelectedTab).Select(tab => tab != null));
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        OpenFolderCommand = ReactiveCommand.CreateFromTask(OpenFolderAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, SelectedTabReady);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync, SelectedTabReady);
        SaveAsFolderCommand = ReactiveCommand.CreateFromTask(SaveAsFolderAsync, SelectedTabReady);
        PackCommand = ReactiveCommand.CreateFromTask(PackAsync, SelectedTabReady);
        UnpackCommand = ReactiveCommand.CreateFromTask(UnpackAsync, SelectedTabReady);
        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync, SelectedTabReady);
        CancelCommand = ReactiveCommand.Create(Cancel,
            this.WhenAnyValue(x => x.SelectedTab)
                .SelectMany(tab => tab != null
                    ? tab.WhenAnyValue(t => t.IsExecuting)
                    : Observable.Return(false)));
        FormatCommand = ReactiveCommand.CreateFromTask(FormatAsync, SelectedTabReady);
        ManageReferencesCommand = ReactiveCommand.Create(OpenReferenceManager, SelectedTabReady);
        OpenSettingsCommand = ReactiveCommand.Create(OpenSettings);
        ExitCommand = ReactiveCommand.Create(Exit);

        _ = RestoreSessionAsync();
    }

    public ObservableCollection<ScriptTabViewModel> Tabs { get; }

    public ScriptTabViewModel? SelectedTab
    {
        get => selectedTab;
        set
        {
            if (selectedTab != null)
                selectedTab.PropertyChanged -= OnSelectedTabPropertyChanged;

            this.RaiseAndSetIfChanged(ref selectedTab, value);

            if (selectedTab != null)
                selectedTab.PropertyChanged += OnSelectedTabPropertyChanged;

            UpdateTabSelectionStates();

            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusBarPath));
            this.RaisePropertyChanged(nameof(CursorPosition));
        }
    }

    public string StatusText => SelectedTab?.StatusText ?? "Ready";

    public string StatusBarPath =>
        SelectedTab != null ? $"ScratchpadSharp › {SelectedTab.Title}" : "ScratchpadSharp";

    public string CursorPosition => SelectedTab?.CursorPosition ?? "1:1";

    public Window? MainWindow
    {
        get => mainWindow;
        set => this.RaiseAndSetIfChanged(ref mainWindow, value);
    }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseTabCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> PackCommand { get; }
    public ReactiveCommand<Unit, Unit> UnpackCommand { get; }
    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> FormatCommand { get; }
    public ReactiveCommand<Unit, Unit> ManageReferencesCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    private IObservable<bool> SelectedTabReady =>
        this.WhenAnyValue(x => x.SelectedTab)
            .SelectMany(tab => tab != null
                ? tab.WhenAnyValue(t => t.IsProjectReady)
                : Observable.Return(false));

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptTabViewModel.StatusText))
            this.RaisePropertyChanged(nameof(StatusText));
        if (e.PropertyName == nameof(ScriptTabViewModel.Title))
            this.RaisePropertyChanged(nameof(StatusBarPath));
        if (e.PropertyName == nameof(ScriptTabViewModel.CursorPosition))
            this.RaisePropertyChanged(nameof(CursorPosition));
    }

    private void UpdateTabSelectionStates()
    {
        foreach (var tab in Tabs)
            tab.IsSelected = tab == selectedTab;
    }

    public void AddTab()
    {
        var tab = CreateTab();
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private ScriptTabViewModel CreateTab(bool deferInitialization = false)
    {
        var tab = new ScriptTabViewModel(scriptService, deferInitialization);
        tab.BindCloseHandler(() => CloseTab(tab));
        return tab;
    }

    private async Task RestoreSessionAsync()
    {
        if (!ApplicationSettings.RestoreSessionOnStartup)
        {
            AddTab();
            return;
        }

        var session = SessionPersistenceService.Load();
        if (session?.Tabs is not { Count: > 0 })
        {
            AddTab();
            return;
        }

        ScriptTabViewModel? selectedTab = null;

        for (var i = 0; i < session.Tabs.Count; i++)
        {
            var tab = CreateTab(deferInitialization: true);
            Tabs.Add(tab);
            await tab.RestoreFromSessionAsync(session.Tabs[i]);

            if (i == session.SelectedTabIndex)
                selectedTab = tab;
        }

        SelectedTab = selectedTab ?? Tabs.Last();
    }

    public void SaveSession()
    {
        if (!ApplicationSettings.RestoreSessionOnStartup || Tabs.Count == 0)
            return;

        var selectedIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : 0;
        if (selectedIndex < 0)
            selectedIndex = 0;

        var session = new ApplicationSession
        {
            SelectedTabIndex = selectedIndex,
            Tabs = Tabs.Select(tab => new TabSessionState
            {
                SourcePath = string.IsNullOrWhiteSpace(tab.ProjectContext.SourcePath)
                    ? null
                    : tab.ProjectContext.SourcePath,
                Code = tab.CodeText,
                Title = tab.Title,
                Config = tab.ProjectContext.Config.Clone(),
                Manifest = tab.ProjectContext.Manifest
            }).ToList()
        };

        SessionPersistenceService.Save(session);
    }

    public void CloseTab(ScriptTabViewModel tab)
    {
        tab.Cleanup();
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
            AddTab();
        else if (SelectedTab == tab || SelectedTab == null)
            SelectedTab = Tabs.Last();
    }

    private void CloseSelectedTab()
    {
        if (SelectedTab != null)
            CloseTab(SelectedTab);
    }

    private async Task OpenAsync()
    {
        if (SelectedTab == null) return;

        try
        {
            SelectedTab.StatusText = "Opening file...";

            var filePath = await ShowOpenFileDialogAsync();
            if (filePath == null)
            {
                SelectedTab.StatusText = "Open cancelled";
                return;
            }

            await SelectedTab.OpenFileAsync(filePath);
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.Output = $"Error opening file: {ex.Message}";
            SelectedTab.StatusText = "Error opening file";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task OpenFolderAsync()
    {
        if (SelectedTab == null) return;

        try
        {
            SelectedTab.StatusText = "Opening folder package...";
            var folderPath = await ShowOpenFolderDialogAsync("Open Developer Mode package folder");
            if (folderPath == null)
            {
                SelectedTab.StatusText = "Open cancelled";
                return;
            }

            if (!PackageService.Instance.IsFolderPackage(folderPath))
            {
                SelectedTab.StatusText = "Not a Scratchpad folder package (.lqpkg/manifest.json missing)";
                this.RaisePropertyChanged(nameof(StatusText));
                return;
            }

            await SelectedTab.OpenFileAsync(folderPath);
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.Output = $"Error opening folder: {ex.Message}";
            SelectedTab.StatusText = "Error opening folder";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task SaveAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        if (string.IsNullOrEmpty(SelectedTab.ProjectContext.SourcePath))
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            SelectedTab.StatusText = "Saving...";
            await SelectedTab.SaveAsync();
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.StatusText = $"Save failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task SaveAsAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        try
        {
            var filePath = await ShowSaveFileDialogAsync();
            if (string.IsNullOrEmpty(filePath)) return;

            SelectedTab.SetSourcePath(filePath);
            await SaveAsync();
        }
        catch (Exception ex)
        {
            SelectedTab.StatusText = $"Save As failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task SaveAsFolderAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        try
        {
            var folderPath = await ShowOpenFolderDialogAsync("Save as Developer Mode folder");
            if (string.IsNullOrEmpty(folderPath)) return;

            SelectedTab.SetSourcePath(folderPath);
            await SaveAsync();
            SelectedTab.StatusText = $"Saved developer folder: {Path.GetFileName(folderPath)}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.StatusText = $"Save as folder failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task PackAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        try
        {
            var sourcePath = SelectedTab.ProjectContext.SourcePath;
            if (string.IsNullOrEmpty(sourcePath) || !PackageService.Instance.IsFolderPackage(sourcePath))
            {
                SelectedTab.StatusText = "Pack needs a Developer Mode folder — use Save as Folder first";
                this.RaisePropertyChanged(nameof(StatusText));
                return;
            }

            await SelectedTab.SaveAsync();

            var zipPath = await ShowSavePackageDialogAsync();
            if (string.IsNullOrEmpty(zipPath)) return;

            if (!zipPath.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase))
                zipPath += ".lqpkg";

            SelectedTab.StatusText = "Packing...";
            await PackageService.Instance.PackAsync(sourcePath, zipPath);
            SelectedTab.StatusText = $"Packed: {Path.GetFileName(zipPath)}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.StatusText = $"Pack failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task UnpackAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        try
        {
            var sourcePath = SelectedTab.ProjectContext.SourcePath;
            if (string.IsNullOrEmpty(sourcePath) || !PackageService.Instance.IsZipPackage(sourcePath))
            {
                SelectedTab.StatusText = "Unpack needs an open .lqpkg file";
                this.RaisePropertyChanged(nameof(StatusText));
                return;
            }

            var folderPath = await ShowOpenFolderDialogAsync("Unpack to folder");
            if (string.IsNullOrEmpty(folderPath)) return;

            SelectedTab.StatusText = "Unpacking...";
            await PackageService.Instance.UnpackAsync(sourcePath, folderPath);
            await SelectedTab.OpenFileAsync(folderPath);
            SelectedTab.StatusText = $"Unpacked to: {Path.GetFileName(folderPath)}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            SelectedTab.StatusText = $"Unpack failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private async Task ExecuteAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;
        await SelectedTab.RunExecuteAsync();
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void Cancel()
    {
        if (SelectedTab == null) return;
        SelectedTab.CancelExecution();
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private async Task FormatAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;
        await SelectedTab.RunFormatAsync();
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void OpenReferenceManager()
    {
        if (MainWindow == null || SelectedTab is not { IsProjectReady: true }) return;

        var vm = new ReferenceManagementViewModel(SelectedTab.TabId, SelectedTab.ProjectContext);
        var window = new Views.ReferenceManagementWindow { DataContext = vm };
        window.ShowDialog(MainWindow);
    }

    private void OpenSettings()
    {
        if (MainWindow == null) return;

        var window = new Views.SettingsWindow { DataContext = new SettingsViewModel() };
        window.ShowDialog(MainWindow);
    }

    private void Exit()
    {
        MainWindow?.Close();
    }

    public void CleanupAllTabs()
    {
        foreach (var tab in Tabs.ToList())
            tab.Cleanup();
    }

    private async Task<string?> ShowOpenFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Scratchpad Script") { Patterns = ["*.cs", "*.lqpkg"] }
            ]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        if (MainWindow?.StorageProvider == null) return null;

        var folders = await MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> ShowSaveFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script",
            DefaultExtension = "cs",
            FileTypeChoices =
            [
                new FilePickerFileType("C# Script") { Patterns = ["*.cs"] },
                new FilePickerFileType("Script Package") { Patterns = ["*.lqpkg"] }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async Task<string?> ShowSavePackageDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Pack to .lqpkg",
            DefaultExtension = "lqpkg",
            FileTypeChoices =
            [
                new FilePickerFileType("Script Package") { Patterns = ["*.lqpkg"] }
            ]
        });

        return file?.Path.LocalPath;
    }
}
