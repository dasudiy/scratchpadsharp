using Microsoft.Extensions.Configuration;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Configuration;

/// <summary>
/// Loads application-wide defaults from appsettings.json for new script packages.
/// </summary>
public static class ConfigurationLoader
{
    private static ScriptConfig _defaults = new();

    public static void Initialize(IConfiguration configuration)
    {
        var defaults = new ScriptConfig();
        configuration.GetSection("ScriptDefaults").Bind(defaults);

        if (defaults.TimeoutSeconds == 0)
        {
            var executionTimeout = configuration.GetValue<int?>("Execution:DefaultTimeoutSeconds");
            if (executionTimeout is > 0)
                defaults.TimeoutSeconds = executionTimeout.Value;
        }

        defaults.DatabaseProvider = DatabaseProviderCatalog.InferProviderId(defaults);
        _defaults = defaults;
    }

    public static ScriptConfig CreateDefaultConfig() => _defaults.Clone();

    /// <summary>Per-query empty connection string inherits ScriptDefaults at run time.</summary>
    public static string ResolveConnectionString(ScriptConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
            return config.ConnectionString;

        return _defaults.ConnectionString ?? string.Empty;
    }
}
