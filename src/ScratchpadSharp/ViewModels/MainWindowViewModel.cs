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
    private ProjectContext projectContext = null!;
    private bool isProjectReady;
    private Window? mainWindow;


    private readonly IScriptExecutionService scriptService;
    private readonly Services.HtmlDumpService? htmlDumpService; // Fixed: added field

    private string htmlOutput = string.Empty;
    private bool showHtmlOutput = true;
    private bool isOutputPanelExpanded = true;

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
        set
        {
            this.RaiseAndSetIfChanged(ref output, value);
            this.RaisePropertyChanged(nameof(OutputDisplayText));
        }
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
        set
        {
            this.RaiseAndSetIfChanged(ref showHtmlOutput, value);
            this.RaisePropertyChanged(nameof(OutputViewToggleLabel));
        }
    }

    public bool IsOutputPanelExpanded
    {
        get => isOutputPanelExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref isOutputPanelExpanded, value);
            this.RaisePropertyChanged(nameof(OutputPanelToggleGlyph));
        }
    }

    public string OutputViewToggleLabel => ShowHtmlOutput ? "Text" : "HTML";

    public string OutputDisplayText =>
        string.IsNullOrWhiteSpace(Output)
            ? "(No output yet — run the script with Console.WriteLine() or .Dump())"
            : Output;

    public string OutputPanelToggleGlyph => IsOutputPanelExpanded ? "−" : "+";

    public string HtmlOutput
    {
        get => htmlOutput;
        set => this.RaiseAndSetIfChanged(ref htmlOutput, value);
    }

    public ProjectContext ProjectContext => projectContext;

    public bool IsProjectReady => isProjectReady;

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
    public ReactiveCommand<Unit, Unit> ToggleOutputPanelCommand { get; }

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
        _ = InitializeProjectAsync();

        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync,
            this.WhenAnyValue(x => x.IsExecuting, executing => !executing));
        NewCommand = ReactiveCommand.CreateFromTask(NewAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        FormatCommand = ReactiveCommand.CreateFromTask(FormatCodeAsync);
        ManageReferencesCommand = ReactiveCommand.Create(OpenReferenceManager);
        ToggleOutputViewCommand = ReactiveCommand.Create(() => { ShowHtmlOutput = !ShowHtmlOutput; });
        ToggleOutputPanelCommand = ReactiveCommand.Create(() => { IsOutputPanelExpanded = !IsOutputPanelExpanded; });
        ExitCommand = ReactiveCommand.Create(() => { System.Diagnostics.Process.GetCurrentProcess().Kill(); });
    }

    private async Task InitializeProjectAsync()
    {
        projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
        isProjectReady = true;
    }

    private async Task NewAsync()
    {
        isProjectReady = false;
        try
        {
            projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
            CodeText = string.Empty;
            Output = string.Empty;
            StatusText = "New file created";
            htmlDumpService?.Clear();
        }
        finally
        {
            isProjectReady = true;
        }
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

            isProjectReady = false;
            try
            {
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
            finally
            {
                isProjectReady = true;
            }
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
                Output = CombineOutput(result.Output, htmlDumpService?.TextOutput);
                projectContext.Output = Output;
                StatusText = "Execution completed successfully";
            }
            else
            {
                Output = CombineOutput(
                    $"Error:\n{result.ErrorMessage}\n\n{result.Output}",
                    htmlDumpService?.TextOutput);
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

    private static string CombineOutput(string consoleOutput, string? dumpText)
    {
        var dumps = dumpText?.TrimEnd();
        var console = consoleOutput.TrimEnd();

        if (string.IsNullOrEmpty(console)) return dumps ?? string.Empty;
        if (string.IsNullOrEmpty(dumps)) return console;
        return console + "\n\n" + dumps;
    }
}