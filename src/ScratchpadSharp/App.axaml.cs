using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScratchpadSharp.Views;
using ScratchpadSharp.ViewModels;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize Roslyn workspace asynchronously to avoid blocking UI
        _ = Task.Run(async () => await RoslynWorkspaceService.Instance.InitializeAsync());

        var lifetime = ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (lifetime != null)
        {
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            viewModel.MainWindow = mainWindow;
            lifetime.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
