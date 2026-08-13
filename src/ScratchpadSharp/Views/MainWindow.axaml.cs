using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        Closing += OnClosing;
    }

    private bool allowClose;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (allowClose)
        {
            vm.SaveSession();
            vm.CleanupAllTabs();
            return;
        }

        if (vm.HasDirtyTabs && !ApplicationSettings.RestoreSessionOnStartup)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await vm.ConfirmDiscardUnsavedAsync("Close ScratchpadSharp"))
                {
                    allowClose = true;
                    Close();
                }
            });
            return;
        }

        vm.SaveSession();
        vm.CleanupAllTabs();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var uri = new Uri("avares://ScratchpadSharp/Assets/app-icon.png");
            Icon = new WindowIcon(AssetLoader.Open(uri));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load window icon: {ex.Message}");
        }
    }
}
