using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Reactive;
using ReactiveUI;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private string output = string.Empty;
    private string statusText = "Ready";
    private bool isExecuting;
    private string codeText = string.Empty;
    private ScriptPackage currentPackage;
    private string? currentFilePath;

    private readonly IScriptExecutionService scriptService;
    private readonly IPackageService packageService;

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

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public MainWindowViewModel() : this(new ScriptExecutionService(), new PackageService())
    {
    }

    public MainWindowViewModel(IScriptExecutionService scriptService, IPackageService packageService)
    {
        this.scriptService = scriptService;
        this.packageService = packageService;

        codeText = string.Empty;
        currentPackage = new ScriptPackage();

        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync, this.WhenAnyValue(x => x.IsExecuting, executing => !executing));
        NewCommand = ReactiveCommand.Create(New);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExitCommand = ReactiveCommand.Create(() =>
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        });
    }

    private void New()
    {
        CodeText = string.Empty;
        Output = string.Empty;
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

    private async Task ExecuteAsync()
    {
        IsExecuting = true;
        try
        {
            StatusText = "Executing...";

            var code = CodeText;
            var config = currentPackage.Config;

            if (config.DefaultUsings.Count == 0)
            {
                config.DefaultUsings = new List<string>
                {
                    "System",
                    "System.Linq",
                    "System.Collections.Generic"
                };
            }

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
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Open Script",
            Filters = new List<Avalonia.Controls.FileDialogFilter>
            {
                new Avalonia.Controls.FileDialogFilter
                {
                    Name = "LinqPad Query Packages",
                    Extensions = new List<string> { "lqpkg" }
                },
                new Avalonia.Controls.FileDialogFilter
                {
                    Name = "C# Scripts",
                    Extensions = new List<string> { "cs", "csx" }
                }
            }
        };

        // This would be called from MainWindow in real implementation
        // For now, we'll use a simple approach
        return null;
    }

    private async Task<string?> ShowSaveFileDialogAsync()
    {
        var dialog = new Avalonia.Controls.SaveFileDialog
        {
            Title = "Save Script",
            Filters = new List<Avalonia.Controls.FileDialogFilter>
            {
                new Avalonia.Controls.FileDialogFilter
                {
                    Name = "LinqPad Query Packages",
                    Extensions = new List<string> { "lqpkg" }
                },
                new Avalonia.Controls.FileDialogFilter
                {
                    Name = "C# Scripts",
                    Extensions = new List<string> { "cs" }
                }
            }
        };

        // This would be called from MainWindow in real implementation
        // For now, we'll use a simple approach
        return null;
    }
}
