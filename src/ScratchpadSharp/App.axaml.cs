using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScratchpadSharp.Views;
using ScratchpadSharp.ViewModels;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using Microsoft.Extensions.Configuration;

namespace ScratchpadSharp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("SCRATCHPAD_");

        IConfiguration config = builder.Build();

        _ = Task.Run(async () =>
        {
            BclXmlResolver.Initialize(config);
            await RoslynWorkspaceService.Instance.InitializeAsync();
        });

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
}
