using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.Services;

namespace ScratchpadSharp.ViewModels;

public class ScriptTabViewModel : ReactiveObject
{
    private string title = "Untitled";
    private string output = string.Empty;
    private string statusText = "Ready";
    private bool isExecuting;
    private string codeText = string.Empty;
    private ProjectContext projectContext = null!;
    private bool isProjectReady;
    private string htmlOutput = string.Empty;
    private bool showHtmlOutput = true;
    private bool isOutputPanelExpanded = true;
    private string cursorPosition = "1:1";

    private readonly IScriptExecutionService scriptService;
    private readonly HtmlDumpService htmlDumpService;

    public ScriptTabViewModel(IScriptExecutionService scriptService)
    {
        this.scriptService = scriptService;
        htmlDumpService = new HtmlDumpService();
        htmlDumpService.SetUpdateCallback(html =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HtmlOutput = html);
        });

        TabId = Guid.NewGuid().ToString("N");

        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync,
            this.WhenAnyValue(
                x => x.IsExecuting,
                x => x.IsProjectReady,
                (bool executing, bool ready) => !executing && ready));
        FormatCommand = ReactiveCommand.CreateFromTask(FormatCodeAsync,
            this.WhenAnyValue(x => x.IsProjectReady));
        ToggleOutputViewCommand = ReactiveCommand.Create(() => { ShowHtmlOutput = !ShowHtmlOutput; });
        ToggleOutputPanelCommand = ReactiveCommand.Create(() => { IsOutputPanelExpanded = !IsOutputPanelExpanded; });
        CloseCommand = ReactiveCommand.Create(() => { });

        _ = InitializeProjectAsync();
    }

    public void BindCloseHandler(Action closeHandler)
    {
        CloseCommand = ReactiveCommand.Create(closeHandler);
    }

    public string TabId { get; }

    public string Title
    {
        get => title;
        set => this.RaiseAndSetIfChanged(ref title, value);
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

    public string CursorPosition
    {
        get => cursorPosition;
        set => this.RaiseAndSetIfChanged(ref cursorPosition, value);
    }

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> FormatCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOutputViewCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOutputPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;

    public async Task InitializeProjectAsync()
    {
        projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
        isProjectReady = true;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        this.RaisePropertyChanged(nameof(ProjectContext));
    }

    public async Task ResetToNewAsync()
    {
        isProjectReady = false;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        try
        {
            projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
            CodeText = string.Empty;
            Output = string.Empty;
            Title = "Untitled";
            StatusText = "New file created";
            htmlDumpService.Clear();
        }
        finally
        {
            isProjectReady = true;
            this.RaisePropertyChanged(nameof(IsProjectReady));
            this.RaisePropertyChanged(nameof(ProjectContext));
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        isProjectReady = false;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        try
        {
            if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
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

            Title = Path.GetFileName(filePath);
            StatusText = $"Opened: {Title}";
            htmlDumpService.Clear();
        }
        finally
        {
            isProjectReady = true;
            this.RaisePropertyChanged(nameof(IsProjectReady));
            this.RaisePropertyChanged(nameof(ProjectContext));
        }
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(projectContext.SourcePath))
            throw new InvalidOperationException("No file path set. Use Save As first.");

        projectContext.Code = CodeText;

        if (projectContext.SourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            await File.WriteAllTextAsync(projectContext.SourcePath, CodeText);
        else
            await ProjectService.Instance.SaveProjectAsync(projectContext);

        Title = Path.GetFileName(projectContext.SourcePath);
        StatusText = $"Saved: {Title}";
    }

    public void SetSourcePath(string filePath)
    {
        projectContext.SourcePath = filePath;
        Title = Path.GetFileName(filePath);
    }

    public void Cleanup()
    {
        RoslynWorkspaceService.Instance.RemoveProject(TabId);
    }

    public Task RunExecuteAsync() => ExecuteAsync();

    public Task RunFormatAsync() => FormatCodeAsync();

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
            Output = string.Empty;
            htmlDumpService.Clear();

            var result = await scriptService.ExecuteAsync(CodeText, projectContext, htmlDumpService.DumpSink);

            if (result.Success)
            {
                Output = CombineOutput(result.Output, htmlDumpService.TextOutput);
                projectContext.Output = Output;
                StatusText = "Execution completed successfully";
            }
            else
            {
                Output = CombineOutput(
                    $"Error:\n{result.ErrorMessage}\n\n{result.Output}",
                    htmlDumpService.TextOutput);
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

    private static string CombineOutput(string consoleOutput, string? dumpText)
    {
        var dumps = dumpText?.TrimEnd();
        var console = consoleOutput.TrimEnd();

        if (string.IsNullOrEmpty(console)) return dumps ?? string.Empty;
        if (string.IsNullOrEmpty(dumps)) return console;
        return console + "\n\n" + dumps;
    }
}
