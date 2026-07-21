using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Core.Configuration;

/// <summary>
/// Builds layered configuration (base appsettings.json → appsettings.user.json → env)
/// and re-applies settings when files change on disk.
/// </summary>
public static class AppConfiguration
{
    private static IConfigurationRoot? _root;
    private static IDisposable? _changeRegistration;

    public static IConfiguration Current =>
        _root ?? throw new InvalidOperationException("AppConfiguration has not been initialized.");

    public static IConfiguration Initialize()
    {
        AppPaths.EnsureUserDataDirectory();

        _changeRegistration?.Dispose();

        _root = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(AppPaths.UserSettingsPath, optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("SCRATCHPAD_")
            .Build();

        Apply(_root);

        _changeRegistration = ChangeToken.OnChange(
            () => _root.GetReloadToken(),
            () => Apply(_root));

        return _root;
    }

    private static void Apply(IConfiguration configuration)
    {
        ConfigurationLoader.Initialize(configuration);
        ApplicationSettings.Initialize(configuration);
        BclXmlResolver.Initialize(configuration);
    }
}
