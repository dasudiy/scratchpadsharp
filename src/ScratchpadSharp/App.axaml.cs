using System;
using System.Runtime.InteropServices;
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
        if (!IsOutputWebViewInitFailure(e.Exception))
            return;

        e.Handled = true;
        OutputWebViewInitFailed?.Invoke(GetOutputWebViewInitFailureMessage(e.Exception));
    }

    private static bool IsOutputWebViewInitFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is InvalidOperationException invalid &&
                invalid.Message.Contains("GTK", StringComparison.OrdinalIgnoreCase))
                return true;

            if (current is COMException && IsWebViewStackFrame(current.StackTrace))
                return true;
        }

        return false;
    }

    private static bool IsWebViewStackFrame(string? stackTrace) =>
        !string.IsNullOrEmpty(stackTrace) &&
        (stackTrace.Contains("NativeWebView", StringComparison.Ordinal) ||
         stackTrace.Contains("WebView2", StringComparison.Ordinal) ||
         stackTrace.Contains("WebViewAdapter", StringComparison.Ordinal));

    private static string GetOutputWebViewInitFailureMessage(Exception exception)
    {
        var root = exception;
        while (root.InnerException != null)
            root = root.InnerException;

        return root.Message;
    }
}
