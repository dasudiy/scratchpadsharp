using System;
using System.IO;

namespace ScratchpadSharp.Core.Configuration;

/// <summary>
/// Writable application data paths (outside the bin/output directory).
/// </summary>
public static class AppPaths
{
    public static string UserDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScratchpadSharp");

    /// <summary>
    /// User overrides for appsettings.json. Created only when the user saves settings.
    /// </summary>
    public static string UserSettingsPath { get; } = Path.Combine(UserDataDirectory, "appsettings.user.json");

    /// <summary>Random AES key for Unix secret protection (0600). Unused on Windows (DPAPI).</summary>
    public static string UserSecretKeyPath { get; } = Path.Combine(UserDataDirectory, "user.key");

    public static string ModulesDirectory { get; } = Path.Combine(UserDataDirectory, "modules");

    public static string QueriesDirectory { get; } = Path.Combine(UserDataDirectory, "Queries");

    public static void EnsureUserDataDirectory()
    {
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(ModulesDirectory);
        Directory.CreateDirectory(QueriesDirectory);
    }
}
