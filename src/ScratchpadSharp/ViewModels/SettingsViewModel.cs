using System;
using System.Reactive;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;

namespace ScratchpadSharp.ViewModels;

public class SettingsViewModel : ReactiveObject
{
    private bool restoreSessionOnStartup;
    private string editorFontFamily = string.Empty;
    private decimal editorFontSize;
    private bool showLineNumbers;
    private decimal tabSize;
    private decimal defaultTimeoutSeconds;
    private string statusText = string.Empty;
    private bool isSaving;

    public SettingsViewModel()
    {
        LoadFromEffectiveSettings();

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync,
            this.WhenAnyValue(x => x.IsSaving, saving => !saving));
        ResetToDefaultsCommand = ReactiveCommand.Create(LoadFromEffectiveSettings);
    }

    public bool RestoreSessionOnStartup
    {
        get => restoreSessionOnStartup;
        set => this.RaiseAndSetIfChanged(ref restoreSessionOnStartup, value);
    }

    public string EditorFontFamily
    {
        get => editorFontFamily;
        set => this.RaiseAndSetIfChanged(ref editorFontFamily, value);
    }

    public decimal EditorFontSize
    {
        get => editorFontSize;
        set => this.RaiseAndSetIfChanged(ref editorFontSize, value);
    }

    public bool ShowLineNumbers
    {
        get => showLineNumbers;
        set => this.RaiseAndSetIfChanged(ref showLineNumbers, value);
    }

    public decimal TabSize
    {
        get => tabSize;
        set => this.RaiseAndSetIfChanged(ref tabSize, value);
    }

    public decimal DefaultTimeoutSeconds
    {
        get => defaultTimeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref defaultTimeoutSeconds, value);
    }

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public bool IsSaving
    {
        get => isSaving;
        set => this.RaiseAndSetIfChanged(ref isSaving, value);
    }

    public string UserSettingsPath => AppPaths.UserSettingsPath;

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    private void LoadFromEffectiveSettings()
    {
        RestoreSessionOnStartup = ApplicationSettings.RestoreSessionOnStartup;
        EditorFontFamily = ApplicationSettings.EditorFontFamily;
        EditorFontSize = (decimal)ApplicationSettings.EditorFontSize;
        ShowLineNumbers = ApplicationSettings.ShowLineNumbers;
        TabSize = ApplicationSettings.TabSize;
        DefaultTimeoutSeconds = ApplicationSettings.DefaultTimeoutSeconds;
        StatusText = string.Empty;
    }

    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var patch = new JsonObject
            {
                ["Application"] = new JsonObject
                {
                    ["RestoreSessionOnStartup"] = RestoreSessionOnStartup
                },
                ["Editor"] = new JsonObject
                {
                    ["FontFamily"] = EditorFontFamily,
                    ["FontSize"] = (double)EditorFontSize,
                    ["ShowLineNumbers"] = ShowLineNumbers,
                    ["TabSize"] = (int)TabSize
                },
                ["Execution"] = new JsonObject
                {
                    ["DefaultTimeoutSeconds"] = (int)DefaultTimeoutSeconds
                }
            };

            await UserSettingsStore.SaveOverridesAsync(patch);
            StatusText = $"Saved to {AppPaths.UserSettingsPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
