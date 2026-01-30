using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.Shared.Exceptions;

namespace ScratchpadSharp.Core.Storage;

public interface IPackageService
{
    Task SaveAsync(ScriptPackage package, string path);
    Task<ScriptPackage> LoadAsync(string path);
    Task PackAsync(string folderPath, string zipPath);
    Task UnpackAsync(string zipPath, string folderPath);
    bool IsZipPackage(string path);
    bool IsFolderPackage(string path);
}

public class PackageService : IPackageService
{
    private const string ManifestFileName = "manifest.json";
    private const string CodeFileName = "code.cs";
    private const string ConfigFileName = "config.json";
    private const string OutputFileName = "last_run.txt";
    private const string DeveloperMarkerFileName = ".lqpkg";

    public async Task SaveAsync(ScriptPackage package, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".lqpkg")
            await SaveAsZipAsync(package, path);
        else
            await SaveAsFolderAsync(package, path);
    }

    public async Task<ScriptPackage> LoadAsync(string path)
    {
        if (File.Exists(path) && path.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase))
            return await LoadFromZipAsync(path);

        if (Directory.Exists(path))
        {
            var markerPath = Path.Combine(path, DeveloperMarkerFileName);
            if (Directory.Exists(markerPath) || File.Exists(Path.Combine(markerPath, ManifestFileName)))
                return await LoadFromFolderAsync(path);
        }

        throw new FileNotFoundException($"Package not found at {path}");
    }

    public bool IsZipPackage(string path) => path.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase);

    public bool IsFolderPackage(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var markerPath = Path.Combine(path, DeveloperMarkerFileName);
        return Directory.Exists(markerPath) || File.Exists(Path.Combine(markerPath, ManifestFileName));
    }

    private async Task SaveAsZipAsync(ScriptPackage package, string path)
    {
        var tempPath = $"{path}.tmp";

        try
        {
            using var fileStream = File.Create(tempPath);
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await AddManifestEntryAsync(archive, package.Manifest);
                await AddCodeEntryAsync(archive, package.Code);
                await AddConfigEntryAsync(archive, package.Config);

                if (!string.IsNullOrEmpty(package.Output))
                    await AddOutputEntryAsync(archive, package.Output);
            }

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tempPath, path);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    private async Task SaveAsFolderAsync(ScriptPackage package, string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        var markerFolder = Path.Combine(path, DeveloperMarkerFileName);
        if (!Directory.Exists(markerFolder))
            Directory.CreateDirectory(markerFolder);

        var manifestPath = Path.Combine(markerFolder, ManifestFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(package.Manifest, new JsonSerializerOptions { WriteIndented = true }));

        var codePath = Path.Combine(path, CodeFileName);
        await File.WriteAllTextAsync(codePath, package.Code);

        var configPath = Path.Combine(path, ConfigFileName);
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(package.Config, new JsonSerializerOptions { WriteIndented = true }));

        if (!string.IsNullOrEmpty(package.Output))
        {
            var outputPath = Path.Combine(path, OutputFileName);
            await File.WriteAllTextAsync(outputPath, package.Output);
        }
    }

    private async Task<ScriptPackage> LoadFromZipAsync(string path)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);

            var manifest = await ReadManifestAsync(archive);
            var code = await ReadCodeAsync(archive);
            var config = await ReadConfigAsync(archive);
            var output = await ReadOutputAsync(archive);

            return new ScriptPackage
            {
                Manifest = manifest,
                Code = code,
                Config = config,
                Output = output
            };
        }
        catch (InvalidDataException ex)
        {
            throw new CorruptPackageException("Package file is corrupted or not a valid zip", ex);
        }
        catch (IOException ex)
        {
            throw new PackageException($"Failed to read package: {ex.Message}", ex);
        }
    }

    private async Task<ScriptPackage> LoadFromFolderAsync(string path)
    {
        try
        {
            var markerPath = Path.Combine(path, DeveloperMarkerFileName);
            var manifestPath = Path.Combine(markerPath, ManifestFileName);

            PackageManifest manifest;
            if (File.Exists(manifestPath))
                manifest = JsonSerializer.Deserialize<PackageManifest>(await File.ReadAllTextAsync(manifestPath)) ??
                           new PackageManifest();
            else
                manifest = new PackageManifest();

            var codePath = Path.Combine(path, CodeFileName);
            var code = File.Exists(codePath) ? await File.ReadAllTextAsync(codePath) : string.Empty;

            var configPath = Path.Combine(path, ConfigFileName);
            ScriptConfig config;
            if (File.Exists(configPath))
                config = JsonSerializer.Deserialize<ScriptConfig>(await File.ReadAllTextAsync(configPath)) ??
                         new ScriptConfig();
            else
                config = new ScriptConfig();

            var outputPath = Path.Combine(path, OutputFileName);
            var output = File.Exists(outputPath) ? await File.ReadAllTextAsync(outputPath) : string.Empty;

            return new ScriptPackage
            {
                Manifest = manifest,
                Code = code,
                Config = config,
                Output = output
            };
        }
        catch (IOException ex)
        {
            throw new PackageException($"Failed to read package folder: {ex.Message}", ex);
        }
    }

    public async Task PackAsync(string folderPath, string zipPath)
    {
        var package = await LoadFromFolderAsync(folderPath);
        await SaveAsZipAsync(package, zipPath);
    }

    public async Task UnpackAsync(string zipPath, string folderPath)
    {
        var package = await LoadFromZipAsync(zipPath);
        await SaveAsFolderAsync(package, folderPath);
    }

    private static async Task AddManifestEntryAsync(ZipArchive archive, PackageManifest manifest)
    {
        var entry = archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task AddCodeEntryAsync(ZipArchive archive, string code)
    {
        var entry = archive.CreateEntry(CodeFileName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(code);
    }

    private static async Task AddConfigEntryAsync(ZipArchive archive, ScriptConfig config)
    {
        var entry = archive.CreateEntry(ConfigFileName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, config, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task AddOutputEntryAsync(ZipArchive archive, string output)
    {
        var entry = archive.CreateEntry(OutputFileName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(output);
    }

    private static async Task<PackageManifest> ReadManifestAsync(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestFileName);
        if (entry == null)
            return new PackageManifest();

        using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<PackageManifest>(stream) ?? new PackageManifest();
    }

    private static async Task<string> ReadCodeAsync(ZipArchive archive)
    {
        var entry = archive.GetEntry(CodeFileName);
        if (entry == null)
            return string.Empty;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task<ScriptConfig> ReadConfigAsync(ZipArchive archive)
    {
        var entry = archive.GetEntry(ConfigFileName);
        if (entry == null)
            return new ScriptConfig();

        using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<ScriptConfig>(stream) ?? new ScriptConfig();
    }

    private static async Task<string> ReadOutputAsync(ZipArchive archive)
    {
        var entry = archive.GetEntry(OutputFileName);
        if (entry == null)
            return string.Empty;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
