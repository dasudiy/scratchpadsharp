using Microsoft.Extensions.Configuration;

namespace ScratchpadSharp.Core.Configuration;

public static class ApplicationSettings
{
    public static bool RestoreSessionOnStartup { get; private set; } = true;

    public static void Initialize(IConfiguration configuration)
    {
        RestoreSessionOnStartup = configuration.GetValue("Application:RestoreSessionOnStartup", true);
    }
}
