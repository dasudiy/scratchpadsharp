using System;
using System.Linq;
using Avalonia;
using ReactiveUI.Avalonia;

namespace ScratchpadSharp;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--headless", StringComparison.OrdinalIgnoreCase))
        {
            var headlessArgs = args.Skip(1).ToArray();
            var exitCode = ScratchpadSharp.Core.Headless.HeadlessScriptRunner
                .RunAsync(headlessArgs)
                .GetAwaiter()
                .GetResult();
            Environment.Exit(exitCode);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                EnableMultiTouch = true
            })
            .LogToTrace()
            .UseReactiveUI(_ => { });
}
