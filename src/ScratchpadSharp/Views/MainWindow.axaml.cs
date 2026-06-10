using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.CleanupAllTabs();
        };
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
