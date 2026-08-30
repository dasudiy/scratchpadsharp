using Unit = ReactiveUI.Primitives.RxVoid;
using ReactiveUI;
using ScratchpadSharp.Core.Security;

namespace ScratchpadSharp.ViewModels;

public sealed class SecretPromptViewModel : ReactiveObject
{
    private string secret = string.Empty;

    public SecretPromptViewModel(UserSecretPromptRequest request)
    {
        Title = KindTitle(request.Kind);
        Prompt = $"{KindLabel(request.Kind)} for '{request.ModuleDisplayName}' could not be unlocked on this machine for the current user. Enter it again to continue.";
        ConfirmCommand = ReactiveCommand.Create(Confirm,
            this.WhenAnyValue(x => x.Secret, value => !string.IsNullOrEmpty(value)));
        CancelCommand = ReactiveCommand.Create(() => { });
    }

    public string Title { get; }
    public string Prompt { get; }

    public string Secret
    {
        get => secret;
        set => this.RaiseAndSetIfChanged(ref secret, value);
    }

    public bool WasConfirmed { get; private set; }

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void Confirm()
    {
        if (string.IsNullOrEmpty(Secret))
            return;
        WasConfirmed = true;
    }

    private static string KindTitle(UserSecretKind kind) => kind switch
    {
        UserSecretKind.DatabasePassword => "Database password",
        UserSecretKind.SshPassword => "SSH password",
        UserSecretKind.SshPassphrase => "SSH key passphrase",
        _ => "Password"
    };

    private static string KindLabel(UserSecretKind kind) => kind switch
    {
        UserSecretKind.DatabasePassword => "The database password",
        UserSecretKind.SshPassword => "The SSH password",
        UserSecretKind.SshPassphrase => "The SSH key passphrase",
        _ => "The password"
    };
}
