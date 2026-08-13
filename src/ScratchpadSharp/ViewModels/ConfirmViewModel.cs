using System.Reactive;
using ReactiveUI;

namespace ScratchpadSharp.ViewModels;

public sealed class ConfirmViewModel : ReactiveObject
{
    private string inputText = string.Empty;

    public ConfirmViewModel(string title, string prompt, string? inputDefault = null, bool showInput = false)
    {
        Title = title;
        Prompt = prompt;
        ShowInput = showInput;
        InputText = inputDefault ?? string.Empty;
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(() => { });
    }

    public string Title { get; }
    public string Prompt { get; }
    public bool ShowInput { get; }

    public string InputText
    {
        get => inputText;
        set => this.RaiseAndSetIfChanged(ref inputText, value);
    }

    public bool WasConfirmed { get; private set; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void Confirm() => WasConfirmed = true;
}
