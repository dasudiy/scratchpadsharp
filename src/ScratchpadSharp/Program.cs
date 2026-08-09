using System;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;

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
            // UsePlatformDetect 会自动调用 .UseSkia()，解决 "No rendering system configured" 报错。
            // 它也会自动探测 Wayland 或 X11，并应用上面设置的环境变量。
            .UsePlatformDetect()
            .With(new X11PlatformOptions 
            { 
                EnableMultiTouch = true
            })
            .LogToTrace()
            .UseReactiveUI();
}
