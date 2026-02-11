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

namespace ScratchpadSharp.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private const string TabId = "main";
    private string output = string.Empty;
    private string statusText = "Ready";
    private bool isExecuting;
    private string codeText = string.Empty;
    private ScriptPackage currentPackage;
    private string? currentFilePath;
    private Window? mainWindow;

    private readonly IScriptExecutionService scriptService;
    private readonly IPackageService packageService;
    private readonly CodeFormatterService formatterService;

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

    public ScriptPackage CurrentPackage => currentPackage;

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> FormatCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    private string htmlOutput = string.Empty;
    private bool showHtmlOutput = true;
    private readonly Services.HtmlDumpService? htmlDumpService;

    public string HtmlOutput
    {
        get => htmlOutput;
        set => this.RaiseAndSetIfChanged(ref htmlOutput, value);
    }

    public bool ShowHtmlOutput
    {
        get => showHtmlOutput;
        set => this.RaiseAndSetIfChanged(ref showHtmlOutput, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleOutputViewCommand { get; }

    public MainWindowViewModel() : this(new ScriptExecutionService(), new PackageService(), new CodeFormatterService(), null)
    {
    }

    public MainWindowViewModel(IScriptExecutionService scriptService, IPackageService packageService, CodeFormatterService formatterService, Services.HtmlDumpService? htmlDumpService = null)
    {
        this.scriptService = scriptService;
        this.packageService = packageService;
        this.formatterService = formatterService;
        this.htmlDumpService = htmlDumpService;

        if (this.htmlDumpService != null)
        {
            this.htmlDumpService.SetUpdateCallback(html =>
            {
                // Ensure UI update happens on UI thread if needed, though ReactiveUI properties usually handle it.
                // For safety with async updates:
                Avalonia.Threading.Dispatcher.UIThread.Post(() => HtmlOutput = html);
            });
        }

        codeText = string.Empty;
        currentPackage = new ScriptPackage();

        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync, this.WhenAnyValue(x => x.IsExecuting, executing => !executing));
        NewCommand = ReactiveCommand.Create(New);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        FormatCommand = ReactiveCommand.CreateFromTask(FormatCodeAsync);
        ToggleOutputViewCommand = ReactiveCommand.Create(() => { ShowHtmlOutput = !ShowHtmlOutput; });
        ExitCommand = ReactiveCommand.Create(() =>
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        });
    }

    private void New()
    {
        CodeText = string.Empty;
        Output = string.Empty;
        HtmlOutput = string.Empty;
        htmlDumpService?.Clear();
        currentFilePath = null;
        currentPackage = new ScriptPackage();
        StatusText = "New script created";
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

            currentPackage = await packageService.LoadAsync(filePath);
            CodeText = currentPackage.Code;
            Output = currentPackage.Output;
            currentFilePath = filePath;

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
        try
        {
            if (currentFilePath == null)
            {
                await SaveAsAsync();
                return;
            }

            StatusText = "Saving...";

            currentPackage.Code = CodeText;
            currentPackage.Output = Output;
            currentPackage.Manifest.Modified = DateTime.UtcNow;

            await packageService.SaveAsync(currentPackage, currentFilePath);
            StatusText = $"Saved: {Path.GetFileName(currentFilePath)}";
        }
        catch (Exception ex)
        {
            Output = $"Error saving file: {ex.Message}";
            StatusText = "Error saving file";
        }
    }

    private async Task SaveAsAsync()
    {
        try
        {
            StatusText = "Saving as...";

            var filePath = await ShowSaveFileDialogAsync();
            if (filePath == null)
            {
                StatusText = "Save as cancelled";
                return;
            }

            currentPackage.Code = CodeText;
            currentPackage.Output = Output;
            currentPackage.Manifest.Modified = DateTime.UtcNow;

            if (currentPackage.Manifest.Created == default)
                currentPackage.Manifest.Created = DateTime.UtcNow;

            await packageService.SaveAsync(currentPackage, filePath);
            currentFilePath = filePath;
            StatusText = $"Saved: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            Output = $"Error saving file: {ex.Message}";
            StatusText = "Error saving file";
        }
    }

    private void Cancel()
    {
        IsExecuting = false;
        StatusText = "Execution cancelled";
    }

    private async Task FormatCodeAsync()
    {
        try
        {
            StatusText = "Formatting code...";
            var formatted = await formatterService.FormatCodeAsync(TabId, CodeText);
            CodeText = formatted;
            StatusText = "Code formatted successfully";
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
            var config = currentPackage.Config;

            var result = await scriptService.ExecuteAsync(code, config);

            if (result.Success)
            {
                Output = result.Output;
                currentPackage.Output = result.Output;
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
        if (MainWindow?.StorageProvider == null)
            return null;

        var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Script",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Script Packages")
                {
                    Patterns = new[] { "*.lqpkg" }
                },
                new FilePickerFileType("C# Scripts")
                {
                    Patterns = new[] { "*.cs", "*.csx" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> ShowSaveFileDialogAsync()
    {
        if (MainWindow?.StorageProvider == null)
            return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script",
            DefaultExtension = "lqpkg",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Script Packages")
                {
                    Patterns = new[] { "*.lqpkg" }
                },
                new FilePickerFileType("C# Scripts")
                {
                    Patterns = new[] { "*.cs" }
                }
            }
        });

        return file?.Path.LocalPath;
    }
}
