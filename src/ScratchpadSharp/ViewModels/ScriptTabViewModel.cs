using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
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
    private bool isSelected;
    private IReadOnlyList<CompilationError> compilationErrors = Array.Empty<CompilationError>();

    private readonly IScriptExecutionService scriptService;
    private readonly HtmlDumpService htmlDumpService;
    private CancellationTokenSource? executeCts;

    public ScriptTabViewModel(IScriptExecutionService scriptService, bool deferInitialization = false)
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

        InitializationTask = deferInitialization ? Task.CompletedTask : InitializeProjectAsync();
    }

    public Task InitializationTask { get; }

    public void BindCloseHandler(Action closeHandler)
    {
        CloseCommand = ReactiveCommand.Create(closeHandler);
    }

    public bool IsSelected
    {
        get => isSelected;
        set => this.RaiseAndSetIfChanged(ref isSelected, value);
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
            this.RaisePropertyChanged(nameof(OutputDisplayHtml));
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
            : AnsiToHtml.Strip(Output);

    public string OutputDisplayHtml
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Output))
            {
                return """<pre style="margin:0;padding:12px 8px;font-family:JetBrains Mono,Cascadia Code,Consolas,monospace;font-size:12.5px;color:#6E7681;">(No output yet — run the script with Console.WriteLine() or .Dump())</pre>""";
            }

            return AnsiToHtml.Convert(Output);
        }
    }

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

    public IReadOnlyList<CompilationError> CompilationErrors
    {
        get => compilationErrors;
        private set => this.RaiseAndSetIfChanged(ref compilationErrors, value);
    }

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> FormatCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOutputViewCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOutputPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;

    public async Task InitializeProjectAsync()
    {
        try
        {
            projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
            MarkProjectReady();
            _ = ResolvePackagesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Project init failed: {ex.Message}";
            throw;
        }
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
            MarkProjectReady();
            _ = ResolvePackagesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"New file failed: {ex.Message}";
            MarkProjectReady();
        }
    }

    private void MarkProjectReady()
    {
        isProjectReady = true;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        this.RaisePropertyChanged(nameof(ProjectContext));
    }

    private async Task ResolvePackagesInBackgroundAsync()
    {
        if (projectContext.Config.NuGetPackages.Count == 0)
            return;

        var previousStatus = StatusText;
        StatusText = "Loading packages...";
        try
        {
            await ProjectService.Instance.ResolveConfiguredPackagesAsync(TabId, projectContext);
            if (StatusText == "Loading packages...")
            {
                StatusText = string.IsNullOrEmpty(previousStatus) ||
                             previousStatus == "Loading packages..." ||
                             previousStatus == "Ready"
                    ? "Ready"
                    : previousStatus;
            }

            this.RaisePropertyChanged(nameof(ProjectContext));
        }
        catch (Exception ex)
        {
            StatusText = $"Package load failed: {ex.Message}";
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        isProjectReady = false;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        try
        {
            var needsPackageResolve = false;
            if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                projectContext = await ProjectService.Instance.NewProjectAsync(TabId);
                projectContext.SourcePath = filePath;
                CodeText = await File.ReadAllTextAsync(filePath);
                Output = string.Empty;
                needsPackageResolve = true;
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
            MarkProjectReady();
            if (needsPackageResolve)
                _ = ResolvePackagesInBackgroundAsync();
        }
        finally
        {
            if (!isProjectReady)
                MarkProjectReady();
        }
    }

    public async Task RestoreFromSessionAsync(TabSessionState state)
    {
        isProjectReady = false;
        this.RaisePropertyChanged(nameof(IsProjectReady));
        try
        {
            projectContext = await ProjectService.Instance.CreateShellProjectAsync(TabId);

            if (!string.IsNullOrEmpty(state.SourcePath))
            {
                projectContext.SourcePath = state.SourcePath;

                if (File.Exists(state.SourcePath) || Directory.Exists(state.SourcePath))
                {
                    await ProjectService.Instance.PrepareEffectiveRootForSessionRestoreAsync(
                        projectContext, state.SourcePath);
                }
            }

            if (state.Config != null &&
                state.Manifest?.ResolvedState.Assemblies is { Count: > 0 })
            {
                await ProjectService.Instance.ApplySavedProjectStateAsync(
                    TabId, projectContext, state.Config, state.Manifest);
            }
            else if (state.Config != null)
            {
                await ProjectService.Instance.RestoreConfigAsync(TabId, projectContext, state.Config);
            }

            if (!string.IsNullOrEmpty(state.Code))
            {
                CodeText = state.Code;
                projectContext.Code = state.Code;
            }

            if (!string.IsNullOrEmpty(state.SourcePath))
                Title = Path.GetFileName(state.SourcePath);
            else if (!string.IsNullOrEmpty(state.Title))
                Title = state.Title;
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
        CancelExecution();
        RoslynWorkspaceService.Instance.RemoveProject(TabId);
    }

    public void CancelExecution()
    {
        if (executeCts == null)
            return;

        executeCts.Cancel();
        StatusText = "Cancellation requested";
    }

    public void ClearCompilationErrors()
    {
        if (CompilationErrors.Count == 0)
            return;
        CompilationErrors = Array.Empty<CompilationError>();
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
        executeCts?.Dispose();
        executeCts = new CancellationTokenSource();
        var token = executeCts.Token;

        IsExecuting = true;
        try
        {
            StatusText = "Executing...";
            Output = string.Empty;
            htmlDumpService.Clear();

            var result = await scriptService.ExecuteAsync(CodeText, projectContext, htmlDumpService.DumpSink, token);

            if (token.IsCancellationRequested)
            {
                CompilationErrors = Array.Empty<CompilationError>();
                Output = CombineOutput(result.Output, htmlDumpService.TextOutput);
                StatusText = "Execution cancelled";
            }
            else if (result.Success)
            {
                CompilationErrors = Array.Empty<CompilationError>();
                Output = CombineOutput(result.Output, htmlDumpService.TextOutput);
                projectContext.Output = Output;
                StatusText = "Execution completed successfully";
            }
            else
            {
                CompilationErrors = result.CompilationErrors.Count > 0
                    ? result.CompilationErrors
                    : Array.Empty<CompilationError>();
                Output = CombineOutput(
                    $"Error:\n{result.ErrorMessage}\n\n{result.Output}",
                    htmlDumpService.TextOutput);
                StatusText = CompilationErrors.Count > 0
                    ? $"Compilation failed ({CompilationErrors.Count})"
                    : "Execution failed";
            }
        }
        catch (OperationCanceledException)
        {
            CompilationErrors = Array.Empty<CompilationError>();
            Output = CombineOutput("Execution cancelled", htmlDumpService.TextOutput);
            StatusText = "Execution cancelled";
        }
        catch (Exception ex)
        {
            CompilationErrors = Array.Empty<CompilationError>();
            Output = $"Fatal error: {ex.Message}\n\n{ex.StackTrace}";
            StatusText = "Fatal error";
        }
        finally
        {
            IsExecuting = false;
            executeCts?.Dispose();
            executeCts = null;
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
