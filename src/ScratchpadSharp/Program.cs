using System;
using Avalonia;
using Avalonia.ReactiveUI;

namespace ScratchpadSharp;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 强制设置缩放因子为 1.5。
        // AVALONIA_SCREEN_SCALE_FACTOR 是最通用的环境变量，影响逻辑像素计算。
        Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTOR", "2");

        // 针对某些 Wayland/GNOME 环境，这个变量可以进一步强制全局缩放。
        Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", "2");

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
                // 注意：X11PlatformOptions 中没有 SystemScaleFactor 属性。
                // 缩放完全通过 Main 中的环境变量来控制。
            })
            .LogToTrace()
            .UseReactiveUI();
}
