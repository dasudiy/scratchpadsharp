using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ScratchpadSharp.Core.Configuration;

public static class ApplicationSettings
{
    public static bool RestoreSessionOnStartup { get; private set; } = true;
    public static string DefaultQueryDirectory { get; private set; } = string.Empty;
    public static string EditorFontFamily { get; private set; } = "Cascadia Code";
    public static double EditorFontSize { get; private set; } = 14;
    public static bool ShowLineNumbers { get; private set; } = true;
    public static int TabSize { get; private set; } = 4;
    public static int DefaultTimeoutSeconds { get; private set; } = 30;

    /// <summary>Raised after Initialize applies a (re)loaded configuration.</summary>
    public static event System.Action? Changed;

    public static void Initialize(IConfiguration configuration)
    {
        RestoreSessionOnStartup = configuration.GetValue("Application:RestoreSessionOnStartup", true);
        DefaultQueryDirectory = configuration.GetValue("Application:DefaultQueryDirectory", string.Empty) ?? string.Empty;
        EditorFontFamily = configuration.GetValue("Editor:FontFamily", "Cascadia Code") ?? "Cascadia Code";
        EditorFontSize = configuration.GetValue("Editor:FontSize", 14d);
        ShowLineNumbers = configuration.GetValue("Editor:ShowLineNumbers", true);
        TabSize = configuration.GetValue("Editor:TabSize", 4);
        DefaultTimeoutSeconds = configuration.GetValue("Execution:DefaultTimeoutSeconds", 30);

        Directory.CreateDirectory(GetEffectiveQueryDirectory());

        Changed?.Invoke();
    }

    public static string GetEffectiveQueryDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DefaultQueryDirectory))
            return Environment.ExpandEnvironmentVariables(DefaultQueryDirectory.Trim());

        return AppPaths.QueriesDirectory;
    }
}
