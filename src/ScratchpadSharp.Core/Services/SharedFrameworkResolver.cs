using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ScratchpadSharp.Core.Services;

internal static class SharedFrameworkResolver
{
    public static IEnumerable<string> GetMicrosoftNetCoreAppAssemblyPaths()
    {
        var frameworkDir = ResolveMicrosoftNetCoreAppDirectory();
        if (string.IsNullOrEmpty(frameworkDir) || !Directory.Exists(frameworkDir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(frameworkDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (MetadataReferenceProvider.IsManagedAssembly(path))
                yield return path;
        }
    }

    public static string? ResolveMicrosoftNetCoreAppDirectory()
    {
        foreach (var dotnetRoot in GetDotNetRoots())
        {
            var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
            if (!Directory.Exists(sharedRoot))
                continue;

            var versionDir = Directory.GetDirectories(sharedRoot)
                .Select(Path.GetFileName)
                .Where(IsCompatibleMajorVersion)
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(versionDir))
                return Path.Combine(sharedRoot, versionDir);
        }

        return null;
    }

    private static bool IsCompatibleMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var major = Environment.Version.Major;
        return version.StartsWith($"{major}.", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetDotNetRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateCandidateDotNetRoots())
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static IEnumerable<string> EnumerateCandidateDotNetRoots()
    {
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)") ?? string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "dotnet");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet";
            yield return "/usr/share/dotnet";
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
        }
    }
}
