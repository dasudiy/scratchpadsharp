using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScratchpadSharp.Core.Security;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class SecretPromptWindow : Window
{
    public SecretPromptWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SecretPromptViewModel { WasConfirmed: true })
            Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}

public sealed class AvaloniaUserSecretPrompt : IUserSecretPrompt
{
    public Task<string?> RequestAsync(UserSecretPromptRequest request, CancellationToken ct = default)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var owner = lifetime?.Windows.FirstOrDefault(w => w.IsActive)
                        ?? lifetime?.MainWindow;
            if (owner == null)
                throw new InvalidOperationException("Cannot prompt for a password without an open window.");

            var vm = new SecretPromptViewModel(request);
            var window = new SecretPromptWindow { DataContext = vm };
            using var cancel = ct.Register(() =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (window.IsVisible)
                        window.Close();
                }));

            await window.ShowDialog(owner);
            return vm.WasConfirmed ? vm.Secret : null;
        });
    }
}
