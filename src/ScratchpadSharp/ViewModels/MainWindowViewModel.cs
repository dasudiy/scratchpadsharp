using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.Core.PackageManagement;
using Splat;


namespace ScratchpadSharp.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private const string TabId = "main";
    private string output = string.Empty;
    private string statusText = "Ready";
    private bool isExecuting;
    private string codeText = string.Empty;
    private ProjectContext projectContext;
    private Window? mainWindow;


    private readonly IScriptExecutionService scriptService;
    private readonly Services.HtmlDumpService? htmlDumpService; // Fixed: added field

    private string htmlOutput = string.Empty;
    private bool showHtmlOutput = true;

    public Window? MainWindow
    {
        get => mainWindow;
        set => this.RaiseAndSetIfChanged(ref mainWindow, value);
    }

    public string CodeText
    {
        get => codeText;
        set => this.RaiseAndSetIfChanged(ref codeText, value);
    }

    public string Output
    {
        get => output;
        set => this.RaiseAndSetIfChanged(ref output, value);
    }

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public bool IsExecuting
    {
        get => isExecuting;
        set => this.RaiseAndSetIfChanged(ref isExecuting, value);
    }


    public bool ShowHtmlOutput
    {
        get => showHtmlOutput;
        set => this.RaiseAndSetIfChanged(ref showHtmlOutput, value);
    }

    public string HtmlOutput
    {
        get => htmlOutput;
        set => this.RaiseAndSetIfChanged(ref htmlOutput, value);
    }

    public ProjectContext ProjectContext => projectContext;

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> FormatCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<Unit, Unit> ManageReferencesCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOutputViewCommand { get; }

    public MainWindowViewModel() : this(new ScriptExecutionService(),
        null)
    {
    }


    public MainWindowViewModel(IScriptExecutionService scriptService, Services.HtmlDumpService? htmlDumpService = null)
    {
        this.scriptService = scriptService;
        this.htmlDumpService = htmlDumpService;

        if (this.htmlDumpService != null)
        {
            this.htmlDumpService.SetUpdateCallback(html =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => HtmlOutput = html);
            });
        }

        codeText = string.Empty;
        ProjectService.Instance.NewProjectAsync(TabId).ContinueWith(t =>
            projectContext = t.Result);

        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync,
            this.WhenAnyValue(x => x.IsExecuting, executing => !executing));
        NewCommand = ReactiveCommand.Create(New);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        FormatCommand = ReactiveCommand.CreateFromTask(FormatCodeAsync);
        ManageReferencesCommand = ReactiveCommand.Create(OpenReferenceManager);
        ToggleOutputViewCommand = ReactiveCommand.Create(() => { ShowHtmlOutput = !ShowHtmlOutput; });
        ExitCommand = ReactiveCommand.Create(() => { System.Diagnostics.Process.GetCurrentProcess().Kill(); });
    }

    private void New()
    {
        ProjectService.Instance.NewProjectAsync(TabId).ContinueWith(t => projectContext = t.Result);
        CodeText = string.Empty;
        Output = string.Empty;
        StatusText = "New file created";
        htmlDumpService?.Clear();
    }

    private async Task OpenAsync()
    {
        try
        {
            StatusText = "Opening file...";

            var filePath = await ShowOpenFileDialogAsync();
            if (filePath == null)
            {
                StatusText = "Open cancelled";
                return;
            }

            if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                // Plain .cs file: create a fresh project and load code only
                projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
                projectContext.SourcePath = filePath;
                CodeText = await File.ReadAllTextAsync(filePath);
                Output = string.Empty;
            }
            else
            {
                projectContext = await ProjectService.Instance.LoadProjectAsync(TabId, filePath);
                CodeText = projectContext.Code;
                Output = projectContext.Output;
            }

            StatusText = $"Opened: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            Output = $"Error opening file: {ex.Message}";
            StatusText = "Error opening file";
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(projectContext.SourcePath))
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            StatusText = "Saving...";
            projectContext.Code = CodeText;

            if (projectContext.SourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                // Plain .cs file: write code only
                await File.WriteAllTextAsync(projectContext.SourcePath, CodeText);
            }
            else
            {
                await ProjectService.Instance.SaveProjectAsync(projectContext);
            }

            StatusText = $"Saved: {Path.GetFileName(projectContext.SourcePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private async Task SaveAsAsync()
    {
        try
        {
            var filePath = await ShowSaveFileDialogAsync();
            if (string.IsNullOrEmpty(filePath)) return;
            projectContext.SourcePath = filePath;
            await SaveAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Save As failed: {ex.Message}";
        }
    }

    private void Cancel()
    {
        StatusText = "Cancellation requested";
        IsExecuting = false;
        // Ideally we would trigger a CancellationTokenSource cancel here
    }

    private async Task FormatCodeAsync()
    {
        try
        {
            CodeText = await CodeFormatterService.FormatCodeAsync(TabId, CodeText);
            StatusText = "Code formatted";
        }
        catch (Exception ex)
        {
            StatusText = $"Format failed: {ex.Message}";
        }
    }

    private async Task ExecuteAsync()
    {
        IsExecuting = true;
        try
        {
            StatusText = "Executing...";

            // Clear previous outputs
            Output = string.Empty;
            htmlDumpService?.Clear();

            var code = CodeText;
            var result = await scriptService.ExecuteAsync(code, projectContext);

            if (result.Success)
            {
                Output = result.Output;
                projectContext.Output = result.Output;
                StatusText = "Execution completed successfully";
            }
            else
            {
                Output = $"Error:\n{result.ErrorMessage}\n\n{result.Output}";
                StatusText = "Execution failed";
            }
        }
        catch (Exception ex)
        {
            Output = $"Fatal error: {ex.Message}\n\n{ex.StackTrace}";
            StatusText = "Fatal error";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task<string?> ShowOpenFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Script",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Scratchpad Script") { Patterns = new[] { "*.cs", "*.lqpkg" } }
            }
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> ShowSaveFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null) return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script",
            DefaultExtension = "cs",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("C# Script") { Patterns = new[] { "*.cs" } },
                new FilePickerFileType("Script Package") { Patterns = new[] { "*.lqpkg" } }
            }
        });

        return file?.Path.LocalPath;
    }

    private void OpenReferenceManager()
    {
        if (MainWindow == null) return;

        var vm = new ReferenceManagementViewModel(TabId, projectContext);

        var window = new Views.ReferenceManagementWindow
        {
            DataContext = vm
        };
        window.ShowDialog(MainWindow);
    }
}