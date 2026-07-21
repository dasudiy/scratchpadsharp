using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ScratchpadSharp.Core.Configuration;

/// <summary>
/// Reads/writes user overrides in appsettings.user.json (copy-on-write: file created on first save).
/// </summary>
public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<JsonObject> LoadOverridesAsync()
    {
        AppPaths.EnsureUserDataDirectory();

        if (!File.Exists(AppPaths.UserSettingsPath))
            return new JsonObject();

        await using var stream = File.OpenRead(AppPaths.UserSettingsPath);
        var node = await JsonNode.ParseAsync(stream);
        return node as JsonObject ?? new JsonObject();
    }

    /// <summary>
    /// Merges <paramref name="patch"/> into the existing user overrides and writes the file.
    /// Nested objects are merged; scalar/array values replace.
    /// </summary>
    public static async Task SaveOverridesAsync(JsonObject patch)
    {
        AppPaths.EnsureUserDataDirectory();

        var existing = await LoadOverridesAsync();
        MergeInto(existing, patch);

        await using var stream = File.Create(AppPaths.UserSettingsPath);
        await JsonSerializer.SerializeAsync(stream, existing, WriteOptions);
    }

    private static void MergeInto(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is JsonObject patchObj && target[key] is JsonObject targetObj)
            {
                MergeInto(targetObj, patchObj);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
