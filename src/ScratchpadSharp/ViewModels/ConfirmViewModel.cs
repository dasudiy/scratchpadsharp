using Unit = ReactiveUI.Primitives.RxVoid;
using ReactiveUI;

namespace ScratchpadSharp.ViewModels;

public enum ConfirmDialogMode
{
    OkCancel,
    UnsavedChanges
}

public enum UnsavedChangesResult
{
    Cancel,
    Save,
    Discard
}

public sealed class ConfirmViewModel : ReactiveObject
{
    private string inputText = string.Empty;

    public ConfirmViewModel(
        string title,
        string prompt,
        string? inputDefault = null,
        bool showInput = false,
        ConfirmDialogMode mode = ConfirmDialogMode.OkCancel,
        string? inputWatermark = null)
    {
        Title = title;
        Prompt = prompt;
        ShowInput = showInput;
        Mode = mode;
        InputWatermark = inputWatermark ?? "Name";
        InputText = inputDefault ?? string.Empty;
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        SaveCommand = ReactiveCommand.Create(Save);
        DiscardCommand = ReactiveCommand.Create(Discard);
        CancelCommand = ReactiveCommand.Create(() => { });
    }

    public string Title { get; }
    public string Prompt { get; }
    public bool ShowInput { get; }
    public ConfirmDialogMode Mode { get; }
    public string InputWatermark { get; }

    public bool ShowOkCancel => Mode == ConfirmDialogMode.OkCancel;
    public bool ShowUnsavedActions => Mode == ConfirmDialogMode.UnsavedChanges;

    public string InputText
    {
        get => inputText;
        set => this.RaiseAndSetIfChanged(ref inputText, value);
    }

    public bool WasConfirmed { get; private set; }
    public UnsavedChangesResult UnsavedResult { get; private set; } = UnsavedChangesResult.Cancel;

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> DiscardCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void Confirm() => WasConfirmed = true;

    private void Save()
    {
        UnsavedResult = UnsavedChangesResult.Save;
        WasConfirmed = true;
    }

    private void Discard()
    {
        UnsavedResult = UnsavedChangesResult.Discard;
        WasConfirmed = true;
    }
}
