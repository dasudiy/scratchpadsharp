using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScratchpadSharp.Views;
using ScratchpadSharp.ViewModels;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Security;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp;

public partial class App : Application
{
    internal static event Action<string>? OutputWebViewInitFailed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

        AppConfiguration.Initialize();
        UserSecretPrompt.Current = new AvaloniaUserSecretPrompt();

        _ = RoslynWorkspaceService.Instance.EnsureInitializedAsync();

        var lifetime = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime != null)
        {
            var viewModel = new MainWindowViewModel(new ScriptExecutionService());
            var mainWindow = new MainWindow { DataContext = viewModel };
            viewModel.MainWindow = mainWindow;
            lifetime.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is not InvalidOperationException { Message: var message }
            || message.IndexOf("GTK", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        e.Handled = true;
        OutputWebViewInitFailed?.Invoke(message);
    }
}
