using Microsoft.Extensions.Configuration;
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

        _defaults = defaults;
    }

    public static ScriptConfig CreateDefaultConfig() => _defaults.Clone();
}
