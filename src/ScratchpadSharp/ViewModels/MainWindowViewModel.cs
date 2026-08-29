using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Dock;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private ScriptTabViewModel? selectedTab;
    private Window? mainWindow;
    private IRootDock? layout;
    private bool suppressCloseConfirm;

    private readonly IScriptExecutionService scriptService;
    private readonly ScratchpadDockFactory dockFactory;

    public MainWindowViewModel(IScriptExecutionService scriptService)
    {
        this.scriptService = scriptService;
        Tabs = new ObservableCollection<ScriptTabViewModel>();

        dockFactory = new ScratchpadDockFactory(
            () => SelectedTab,
            OpenModuleQueryAsync,
            OpenQueryFromTreeAsync,
            () => CreateTab(),
            OnDocumentCreated);

        dockFactory.ActiveDockableChanged += OnActiveDockableChanged;
        dockFactory.DockableClosing += OnDockableClosing;
        dockFactory.DockableClosed += OnDockableClosed;

        var dockLayout = dockFactory.CreateLayout();
        dockFactory.InitLayout(dockLayout);
        Layout = dockLayout;

        ModulesSidebar = dockFactory.ModulesSidebar;
        QueriesSidebar = dockFactory.QueriesSidebar;

        NewTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.CreateFromTask(CloseSelectedTabAsync,
            this.WhenAnyValue(x => x.SelectedTab).Select(tab => tab != null));
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        OpenFolderCommand = ReactiveCommand.CreateFromTask(OpenFolderAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, SelectedTabReady);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync, SelectedTabReady);
        SaveAsFolderCommand = ReactiveCommand.CreateFromTask(SaveAsFolderAsync, SelectedTabReady);
        PackCommand = ReactiveCommand.CreateFromTask(PackAsync, SelectedTabReady);
        UnpackCommand = ReactiveCommand.CreateFromTask(UnpackAsync, SelectedTabReady);
        RenameTabCommand = ReactiveCommand.Create(BeginRenameSelectedTab, CanRenameSelectedTab);
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

        ApplicationSettings.Changed += () => this.RaisePropertyChanged(nameof(IndentLabel));

        _ = RestoreSessionAsync();
    }

    public ModulesSidebarViewModel ModulesSidebar { get; }
    public QueriesSidebarViewModel QueriesSidebar { get; }

    public IRootDock? Layout
    {
        get => layout;
        private set => this.RaiseAndSetIfChanged(ref layout, value);
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
            {
                selectedTab.PropertyChanged += OnSelectedTabPropertyChanged;
                dockFactory.ActivateScriptDocument(selectedTab);
            }

            ModulesSidebar.RefreshReferencedState();

            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusBarPath));
            this.RaisePropertyChanged(nameof(CursorPosition));
            this.RaisePropertyChanged(nameof(IndentLabel));
        }
    }

    public string StatusText => SelectedTab?.StatusText ?? "Ready";

    public string StatusBarPath =>
        SelectedTab != null ? $"ScratchpadSharp › {SelectedTab.Title}" : "ScratchpadSharp";

    public string CursorPosition => SelectedTab?.CursorPosition ?? "1:1";

    public string IndentLabel => $"{ApplicationSettings.TabSize} spaces";

    public Window? MainWindow
    {
        get => mainWindow;
        set
        {
            this.RaiseAndSetIfChanged(ref mainWindow, value);
            QueriesSidebar.ConfigureDialogs(
                () => mainWindow,
                CloseQueryTabsByPathAsync,
                UpdateQueryTabPathAsync,
                SaveQueryByPathAsync,
                ReopenQueryTabsAtPathAsync,
                RetargetQueryTabAsync,
                CreateNewQueryInFolderAsync);
        }
    }

    private async Task<bool> CloseQueryTabsByPathAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var matching = Tabs.Where(tab =>
        {
            var source = tab.ProjectContext.SourcePath;
            return !string.IsNullOrEmpty(source)
                   && string.Equals(Path.GetFullPath(source), fullPath, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        foreach (var tab in matching)
        {
            if (!tab.IsDirty)
                continue;

            var result = await PromptUnsavedChangesAsync("Unsaved changes");
            if (result == UnsavedChangesResult.Cancel)
                return false;

            if (result == UnsavedChangesResult.Save && !await TrySaveTabAsync(tab))
                return false;
        }

        foreach (var tab in matching)
            ForceCloseTab(tab);

        return true;
    }

    private Task UpdateQueryTabPathAsync(string oldPath, string newPath)
    {
        var oldFullPath = Path.GetFullPath(oldPath);
        foreach (var tab in Tabs)
        {
            var source = tab.ProjectContext.SourcePath;
            if (string.IsNullOrEmpty(source))
                continue;

            if (string.Equals(Path.GetFullPath(source), oldFullPath, StringComparison.OrdinalIgnoreCase))
                tab.SetSourcePath(newPath);
        }

        return Task.CompletedTask;
    }

    private async Task ReopenQueryTabsAtPathAsync(string oldPath, string newPath)
    {
        var oldFullPath = Path.GetFullPath(oldPath);
        foreach (var tab in Tabs.ToList())
        {
            var source = tab.ProjectContext.SourcePath;
            if (string.IsNullOrEmpty(source))
                continue;

            if (!string.Equals(Path.GetFullPath(source), oldFullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (tab.IsDirty)
            {
                var result = await PromptUnsavedChangesAsync("Move query");
                if (result == UnsavedChangesResult.Cancel)
                    throw new InvalidOperationException("Move cancelled");

                if (result == UnsavedChangesResult.Save && !await TrySaveTabAsync(tab))
                    throw new InvalidOperationException("Move cancelled");
            }

            tab.SetSourcePath(newPath);
        }
    }

    private async Task RetargetQueryTabAsync(string oldPath, string newPath)
    {
        var tab = FindTabBySourcePath(oldPath);
        if (tab != null)
            await tab.OpenFileAsync(newPath);
        else
            await OpenQueryAtPathAsync(newPath);
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
    public ReactiveCommand<Unit, Unit> RenameTabCommand { get; }
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

    private IObservable<bool> CanRenameSelectedTab =>
        this.WhenAnyValue(x => x.SelectedTab)
            .SelectMany(tab => tab != null
                ? tab.WhenAnyValue(t => t.CanRename)
                : Observable.Return(false));

    private void OnDocumentCreated(ScriptTabViewModel tab, ScriptDocument document)
    {
        tab.BindCloseHandler(() => _ = RequestCloseTabAsync(tab));
        tab.QueryRenameHandler = CommitQueryRenameAsync;
        Tabs.Add(tab);
    }

    private void OnDockableClosing(object? sender, DockableClosingEventArgs e)
    {
        if (suppressCloseConfirm)
            return;

        if (e.Dockable is not ScriptDocument document || !document.Tab.IsDirty)
            return;

        e.Cancel = true;
        var tab = document.Tab;
        Dispatcher.UIThread.Post(async () => await RequestCloseTabAsync(tab));
    }

    private async Task CommitQueryRenameAsync(string oldPath, string newName)
    {
        var newPath = QueryPathOperations.TryRename(oldPath, newName, out var error);
        if (newPath == null)
        {
            if (SelectedTab != null)
                SelectedTab.StatusText = error ?? "Rename failed";
            this.RaisePropertyChanged(nameof(StatusText));
            return;
        }

        await UpdateQueryTabPathAsync(oldPath, newPath);
        QueriesSidebar.RequestRefresh();
        if (SelectedTab != null)
            SelectedTab.StatusText = $"Renamed to {Path.GetFileName(newPath)}";
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void BeginRenameSelectedTab() => SelectedTab?.BeginRename();

    private async Task SaveQueryByPathAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var tab in Tabs)
        {
            var source = tab.ProjectContext.SourcePath;
            if (string.IsNullOrEmpty(source))
                continue;

            if (string.Equals(Path.GetFullPath(source), fullPath, StringComparison.OrdinalIgnoreCase))
                await tab.SaveAsync();
        }
    }

    private void OnActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs e)
    {
        if (e.Dockable is ScriptDocument document && selectedTab != document.Tab)
            SetSelectedTab(document.Tab);
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is not ScriptDocument document)
            return;

        var wasSelected = selectedTab == document.Tab;
        document.Tab.Cleanup();
        Tabs.Remove(document.Tab);

        if (Tabs.Count == 0)
        {
            AddTab();
            return;
        }

        if (wasSelected)
            SyncSelectedTabFromDock();
    }

    private void SyncSelectedTabFromDock()
    {
        var activeTab = dockFactory.GetActiveScriptTab();
        if (activeTab is not null && activeTab != selectedTab)
            SetSelectedTab(activeTab);
    }

    private void SetSelectedTab(ScriptTabViewModel? tab)
    {
        if (selectedTab != null)
            selectedTab.PropertyChanged -= OnSelectedTabPropertyChanged;

        selectedTab = tab;
        this.RaisePropertyChanged(nameof(SelectedTab));

        if (selectedTab != null)
            selectedTab.PropertyChanged += OnSelectedTabPropertyChanged;

        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(StatusBarPath));
        this.RaisePropertyChanged(nameof(CursorPosition));
    }

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptTabViewModel.StatusText))
            this.RaisePropertyChanged(nameof(StatusText));
        if (e.PropertyName == nameof(ScriptTabViewModel.Title))
            this.RaisePropertyChanged(nameof(StatusBarPath));
        if (e.PropertyName == nameof(ScriptTabViewModel.CursorPosition))
            this.RaisePropertyChanged(nameof(CursorPosition));
    }

    public void AddTab()
    {
        var tab = CreateTab();
        dockFactory.AddScriptDocument(tab);
        SelectedTab = tab;
    }

    public async Task OpenModuleQueryAsync(string instanceId, string title, string code)
    {
        var tab = CreateTab(deferInitialization: true);
        dockFactory.AddScriptDocument(tab);
        SelectedTab = tab;
        await tab.OpenModuleQueryAsync(instanceId, title, code, autoRun: true);
    }

    public Task OpenQueryFromTreeAsync(string filePath) => OpenQueryAtPathAsync(filePath);

    private ScriptTabViewModel? FindTabBySourcePath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return Tabs.FirstOrDefault(tab =>
            !string.IsNullOrEmpty(tab.ProjectContext.SourcePath)
            && string.Equals(Path.GetFullPath(tab.ProjectContext.SourcePath), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ScriptTabViewModel?> AcquireTabForNewContentAsync(string promptTitle = "Unsaved changes")
    {
        if (SelectedTab is { IsDirty: true })
        {
            var result = await PromptUnsavedChangesAsync(promptTitle);
            if (result == UnsavedChangesResult.Cancel)
                return null;

            if (result == UnsavedChangesResult.Save && !await TrySaveTabAsync(SelectedTab))
                return null;
        }

        if (SelectedTab is { IsDirty: false } && string.IsNullOrEmpty(SelectedTab.ProjectContext.SourcePath))
            return SelectedTab;

        var tab = CreateTab(deferInitialization: true);
        dockFactory.AddScriptDocument(tab);
        SelectedTab = tab;
        return tab;
    }

    private async Task OpenQueryAtPathAsync(string filePath)
    {
        var existing = FindTabBySourcePath(filePath);
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = await AcquireTabForNewContentAsync("Open query");
        if (tab == null)
            return;

        try
        {
            tab.StatusText = "Opening query...";
            await tab.OpenFileAsync(filePath);
            QueriesSidebar.RequestRefresh();
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            tab.Output = $"Error opening query: {ex.Message}";
            tab.StatusText = "Error opening query";
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public async Task CreateNewQueryInFolderAsync(string parentPath)
    {
        var name = await Views.ConfirmWindow.PromptAsync(MainWindow, "New Query", "Query name:", "script", "Query name");
        if (string.IsNullOrWhiteSpace(name))
            return;

        name = SanitizeFolderName(name);
        var folderPath = AllocateUniqueFolderInParent(parentPath, name);

        var tab = await AcquireTabForNewContentAsync("New query");
        if (tab == null)
            return;

        await tab.InitializationTask;

        Directory.CreateDirectory(folderPath);
        tab.SetSourcePath(folderPath);
        tab.StatusText = "Saving...";
        await tab.SaveAsync();
        QueriesSidebar.RequestRefresh();
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private ScriptTabViewModel CreateTab(bool deferInitialization = false)
    {
        return new ScriptTabViewModel(scriptService, deferInitialization);
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

        ScriptTabViewModel? restoredSelection = null;

        for (var i = 0; i < session.Tabs.Count; i++)
        {
            var tab = CreateTab(deferInitialization: true);
            dockFactory.AddScriptDocument(tab);
            await tab.RestoreFromSessionAsync(session.Tabs[i]);

            if (i == session.SelectedTabIndex)
                restoredSelection = tab;
        }

        SelectedTab = restoredSelection ?? Tabs.Last();
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

    public bool HasDirtyTabs => Tabs.Any(tab => tab.IsDirty);

    public async Task<bool> ConfirmDiscardUnsavedAsync(string? title = null)
    {
        var result = await PromptUnsavedChangesAsync(title ?? "Unsaved changes");
        return result == UnsavedChangesResult.Discard;
    }

    public async Task<bool> RequestCloseTabAsync(ScriptTabViewModel tab)
    {
        if (tab.IsDirty)
        {
            var result = await PromptUnsavedChangesAsync("Unsaved changes");
            if (result == UnsavedChangesResult.Cancel)
                return false;

            if (result == UnsavedChangesResult.Save && !await TrySaveTabAsync(tab))
                return false;
        }

        ForceCloseTab(tab);
        return true;
    }

    private async Task<UnsavedChangesResult> PromptUnsavedChangesAsync(string title)
    {
        return await Views.ConfirmWindow.ShowUnsavedChangesAsync(
            MainWindow,
            title,
            "Save changes before continuing?");
    }

    private async Task<bool> TrySaveTabAsync(ScriptTabViewModel tab)
    {
        if (!tab.IsProjectReady)
            return false;

        if (string.IsNullOrEmpty(tab.ProjectContext.SourcePath))
        {
            var folderPath = AllocateDefaultQueryFolderPath(tab.Title);
            Directory.CreateDirectory(folderPath);
            tab.SetSourcePath(folderPath);
        }

        try
        {
            tab.StatusText = "Saving...";
            await tab.SaveAsync();
            QueriesSidebar.RequestRefresh();
            this.RaisePropertyChanged(nameof(StatusText));
            return true;
        }
        catch (Exception ex)
        {
            tab.StatusText = $"Save failed: {ex.Message}";
            this.RaisePropertyChanged(nameof(StatusText));
            return false;
        }
    }

    public void CloseTab(ScriptTabViewModel tab) => ForceCloseTab(tab);

    private void ForceCloseTab(ScriptTabViewModel tab)
    {
        if (!dockFactory.TryGetDocument(tab, out var document))
            return;

        suppressCloseConfirm = true;
        try
        {
            dockFactory.CloseDockable(document);
        }
        finally
        {
            suppressCloseConfirm = false;
        }
    }

    private async Task CloseSelectedTabAsync()
    {
        if (SelectedTab == null)
            return;

        await RequestCloseTabAsync(SelectedTab);
    }

    private async Task OpenAsync()
    {
        if (SelectedTab == null)
            return;

        try
        {
            SelectedTab.StatusText = "Opening file...";
            var filePath = await ShowOpenFileDialogAsync();
            if (filePath == null)
            {
                SelectedTab.StatusText = "Open cancelled";
                return;
            }

            await OpenQueryAtPathAsync(filePath);
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
        if (SelectedTab == null)
            return;

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

            await OpenQueryAtPathAsync(folderPath);
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
        if (SelectedTab is not { IsProjectReady: true })
            return;

        await TrySaveTabAsync(SelectedTab);
    }

    private async Task SaveAsAsync()
    {
        if (SelectedTab is not { IsProjectReady: true }) return;

        try
        {
            var filePath = await ShowSaveFileDialogAsync();
            if (string.IsNullOrEmpty(filePath)) return;

            if (!filePath.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase))
                filePath += ".lqpkg";

            SelectedTab.SetSourcePath(filePath);
            await SaveAsync();
            QueriesSidebar.RequestRefresh();
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
            var folderPath = await ShowOpenFolderDialogAsync("Choose parent folder for Developer Mode package");
            if (string.IsNullOrEmpty(folderPath)) return;

            var suggested = SanitizeFolderName(SelectedTab.Title);
            var name = await Views.ConfirmWindow.PromptAsync(
                MainWindow,
                "Save as folder",
                "Folder name (leave empty to use the selected folder).",
                suggested,
                "Folder name");
            if (name == null)
                return;
            if (!string.IsNullOrWhiteSpace(name))
                folderPath = Path.Combine(folderPath, SanitizeFolderName(name));

            Directory.CreateDirectory(folderPath);
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

            var zipPath = await QueryPathOperations.ResolvePackTargetAsync(MainWindow, sourcePath);
            if (string.IsNullOrEmpty(zipPath))
                return;

            SelectedTab.StatusText = "Packing...";
            this.RaisePropertyChanged(nameof(StatusText));

            await QueryPathOperations.PackAsync(sourcePath, zipPath);
            QueryPathOperations.DeletePath(sourcePath);
            await SelectedTab.OpenFileAsync(zipPath);
            QueriesSidebar.RequestRefresh();
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

            var folderPath = await QueryPathOperations.ResolveUnpackTargetAsync(MainWindow, sourcePath);
            if (string.IsNullOrEmpty(folderPath))
                return;

            SelectedTab.StatusText = "Unpacking...";
            this.RaisePropertyChanged(nameof(StatusText));

            await QueryPathOperations.UnpackAsync(sourcePath, folderPath);
            QueryPathOperations.DeletePath(sourcePath);
            await SelectedTab.OpenFileAsync(folderPath);
            QueriesSidebar.RequestRefresh();
            SelectedTab.StatusText = $"Unpacked: {Path.GetFileName(folderPath)}";
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

        var window = new Views.SettingsWindow
        {
            DataContext = new SettingsViewModel { StorageProvider = MainWindow.StorageProvider }
        };
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
            SuggestedStartLocation = await GetQueryDirectoryFolderAsync(),
            FileTypeFilter =
            [
                new FilePickerFileType("Script Package") { Patterns = ["*.lqpkg"] }
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
            AllowMultiple = false,
            SuggestedStartLocation = await GetQueryDirectoryFolderAsync()
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> ShowSaveFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script Package",
            DefaultExtension = "lqpkg",
            SuggestedStartLocation = await GetQueryDirectoryFolderAsync(),
            SuggestedFileName = SelectedTab != null ? SanitizeFolderName(SelectedTab.Title) + ".lqpkg" : "query.lqpkg",
            FileTypeChoices =
            [
                new FilePickerFileType("Script Package") { Patterns = ["*.lqpkg"] }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async Task<IStorageFolder?> GetQueryDirectoryFolderAsync()
    {
        if (MainWindow?.StorageProvider == null)
            return null;

        var directory = ApplicationSettings.GetEffectiveQueryDirectory();
        Directory.CreateDirectory(directory);
        return await MainWindow.StorageProvider.TryGetFolderFromPathAsync(directory);
    }

    private static string AllocateDefaultQueryFolderPath(string title)
    {
        var directory = ApplicationSettings.GetEffectiveQueryDirectory();
        return AllocateUniqueFolderInParent(directory, SanitizeFolderName(title));
    }

    private static string AllocateUniqueFolderInParent(string parentPath, string baseName)
    {
        var path = Path.Combine(parentPath, baseName);
        if (!Directory.Exists(path) && !File.Exists(path))
            return path;

        for (var i = 2; ; i++)
        {
            path = Path.Combine(parentPath, $"{baseName}{i}");
            if (!Directory.Exists(path) && !File.Exists(path))
                return path;
        }
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "Untitled")
            return "script";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
