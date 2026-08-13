using System.Text.Json;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public static class SessionPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SessionFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScratchpadSharp",
            "session.json");

    public static ApplicationSession? Load()
    {
        if (!File.Exists(SessionFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(SessionFilePath);
            return JsonSerializer.Deserialize<ApplicationSession>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ApplicationSession session)
    {
        var directory = Path.GetDirectoryName(SessionFilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(session, JsonOptions);
        var tempPath = SessionFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SessionFilePath, overwrite: true);
    }
}
