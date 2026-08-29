using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Views;

namespace ScratchpadSharp.ViewModels;

public static class QueryPathOperations
{
    public static string GetSiblingPackPath(string folderPath)
    {
        var parent = Path.GetDirectoryName(folderPath)!;
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(parent, name + ".lqpkg");
    }

    public static string GetSiblingUnpackPath(string lqpkgPath) =>
        Path.Combine(Path.GetDirectoryName(lqpkgPath)!, Path.GetFileNameWithoutExtension(lqpkgPath));

    public static async Task<string?> ResolvePackTargetAsync(Window? owner, string folderPath)
    {
        var preferred = GetSiblingPackPath(folderPath);
        return await ResolveConflictAsync(owner, preferred, "Pack", isFile: true);
    }

    public static async Task<string?> ResolveUnpackTargetAsync(Window? owner, string lqpkgPath)
    {
        var preferred = GetSiblingUnpackPath(lqpkgPath);
        return await ResolveConflictAsync(owner, preferred, "Unpack", isFile: false);
    }

    public static Task PackAsync(string folderPath, string zipPath) =>
        PackageService.Instance.PackAsync(folderPath, zipPath);

    public static Task UnpackAsync(string lqpkgPath, string folderPath) =>
        PackageService.Instance.UnpackAsync(lqpkgPath, folderPath);

    public static string? TryMove(string sourcePath, string targetDirectoryPath, out string? error)
    {
        error = null;
        sourcePath = Path.GetFullPath(sourcePath);
        targetDirectoryPath = Path.GetFullPath(targetDirectoryPath);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            error = "Source not found";
            return null;
        }

        if (!Directory.Exists(targetDirectoryPath))
        {
            error = "Target folder not found";
            return null;
        }

        var fileName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destPath = Path.Combine(targetDirectoryPath, fileName);

        if (string.Equals(Path.GetDirectoryName(sourcePath), targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        if (File.Exists(destPath) || Directory.Exists(destPath))
        {
            error = $"Already exists: {fileName}";
            return null;
        }

        if (Directory.Exists(sourcePath))
        {
            var sourcePrefix = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var targetPrefix = targetDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (targetPrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "Cannot move a folder into itself or its subfolder";
                return null;
            }
        }

        try
        {
            if (File.Exists(sourcePath))
                File.Move(sourcePath, destPath);
            else
                Directory.Move(sourcePath, destPath);

            return destPath;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static bool CanMoveTo(string sourcePath, string targetDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetDirectoryPath))
            return false;

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return false;

        if (!Directory.Exists(targetDirectoryPath))
            return false;

        sourcePath = Path.GetFullPath(sourcePath);
        targetDirectoryPath = Path.GetFullPath(targetDirectoryPath);

        if (string.Equals(sourcePath, targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var sourceParent = Path.GetDirectoryName(sourcePath);
        if (sourceParent != null
            && string.Equals(sourceParent, targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Directory.Exists(sourcePath))
        {
            var sourcePrefix = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var targetPrefix = targetDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (targetPrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static string? TryRename(string oldPath, string newName, out string? error)
    {
        error = null;
        newName = SanitizeName(newName.Trim());
        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "Name cannot be empty";
            return null;
        }

        var currentName = Path.GetFileName(oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(newName, currentName, StringComparison.Ordinal))
            return oldPath;

        var parent = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(parent, newName);
        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            error = $"Already exists: {newName}";
            return null;
        }

        try
        {
            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
            else
                Directory.Move(oldPath, newPath);

            return newPath;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static void DeletePath(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        else if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static async Task<string?> ResolveConflictAsync(Window? owner, string preferredPath, string title, bool isFile)
    {
        if (!File.Exists(preferredPath) && !Directory.Exists(preferredPath))
            return preferredPath;

        var parent = Path.GetDirectoryName(preferredPath)!;
        var defaultName = isFile
            ? Path.GetFileNameWithoutExtension(preferredPath)
            : Path.GetFileName(preferredPath);

        var name = await ConfirmWindow.PromptAsync(
            owner,
            title,
            $"'{Path.GetFileName(preferredPath)}' already exists. Enter a new name:",
            defaultName);

        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = SanitizeName(name);
        var resolved = isFile ? Path.Combine(parent, name + ".lqpkg") : Path.Combine(parent, name);
        if (File.Exists(resolved) || Directory.Exists(resolved))
            return null;

        return resolved;
    }

    public static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
