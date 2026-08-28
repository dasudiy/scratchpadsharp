using ScratchpadSharp.Core.PackageManagement;

namespace ScratchpadSharp.Core.Tests;

public static class NuGetPackageAssetResolverTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(ResolveRuntimeAssemblyPath_UsesUnixLib), ResolveRuntimeAssemblyPath_UsesUnixLib);
        failures += Run(nameof(ResolveRuntimeAssemblyPath_LibUsesUnixLib), ResolveRuntimeAssemblyPath_LibUsesUnixLib);
        failures += Run(nameof(SelectPreferredRuntimeAssemblies_PrefersUnixOverLib),
            SelectPreferredRuntimeAssemblies_PrefersUnixOverLib);
        failures += Run(nameof(InferPackageRoot_ReturnsNullWithoutNuGetLayout),
            InferPackageRoot_ReturnsNullWithoutNuGetLayout);
        failures += Run(nameof(InferPackageRoot_DetectsLibFolder), InferPackageRoot_DetectsLibFolder);
        failures += Run(nameof(PreferCompileAssemblyPath_UsesRefWhenPresent),
            PreferCompileAssemblyPath_UsesRefWhenPresent);
        failures += Run(nameof(SelectPreferredCompileAssemblies_PrefersRefOverLib),
            SelectPreferredCompileAssemblies_PrefersRefOverLib);
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool ResolveRuntimeAssemblyPath_UsesUnixLib()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var refPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "ref", "net8.0", "Microsoft.Data.SqlClient.dll");
        if (!File.Exists(refPath))
            return true;

        var runtimePath = NuGetPackageAssetResolver.ResolveRuntimeAssemblyPath(refPath);
        var expected = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "runtimes", "unix", "lib", "net8.0", "Microsoft.Data.SqlClient.dll");

        return runtimePath.Equals(expected, StringComparison.Ordinal) && File.Exists(runtimePath);
    }

    private static bool ResolveRuntimeAssemblyPath_LibUsesUnixLib()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var libPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "lib", "net8.0", "Microsoft.Data.SqlClient.dll");
        if (!File.Exists(libPath))
            return true;

        var runtimePath = NuGetPackageAssetResolver.ResolveRuntimeAssemblyPath(libPath);
        var expected = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "runtimes", "unix", "lib", "net8.0", "Microsoft.Data.SqlClient.dll");

        return runtimePath.Equals(expected, StringComparison.Ordinal) && File.Exists(runtimePath);
    }

    private static bool SelectPreferredRuntimeAssemblies_PrefersUnixOverLib()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var libPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "lib", "net8.0", "Microsoft.Data.SqlClient.dll");
        var unixPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "runtimes", "unix", "lib", "net8.0", "Microsoft.Data.SqlClient.dll");
        if (!File.Exists(libPath) || !File.Exists(unixPath))
            return true;

        var selected = NuGetPackageAssetResolver.SelectPreferredRuntimeAssemblies([libPath, unixPath]);
        return selected.Count == 1 && selected[0].Equals(unixPath, StringComparison.Ordinal);
    }

    private static bool InferPackageRoot_ReturnsNullWithoutNuGetLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), "scratchpad-local", "MyLib.dll");
        return NuGetPackageAssetResolver.InferPackageRoot(path) == null;
    }

    private static bool InferPackageRoot_DetectsLibFolder()
    {
        var path = Path.Combine("cache", "newtonsoft.json", "13.0.3", "lib", "net6.0", "Newtonsoft.Json.dll");
        var root = NuGetPackageAssetResolver.InferPackageRoot(path);
        return root != null &&
               root.Replace('\\', '/').EndsWith("newtonsoft.json/13.0.3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PreferCompileAssemblyPath_UsesRefWhenPresent()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var libPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "lib", "net8.0", "Microsoft.Data.SqlClient.dll");
        var refPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "ref", "net8.0", "Microsoft.Data.SqlClient.dll");
        if (!File.Exists(libPath) || !File.Exists(refPath))
            return true;

        var compile = NuGetPackageAssetResolver.PreferCompileAssemblyPath(libPath);
        return compile.Equals(refPath, StringComparison.Ordinal);
    }

    private static bool SelectPreferredCompileAssemblies_PrefersRefOverLib()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var libPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "lib", "net8.0", "Microsoft.Data.SqlClient.dll");
        var refPath = Path.Combine(
            home, ".nuget", "packages", "microsoft.data.sqlclient", "5.2.2",
            "ref", "net8.0", "Microsoft.Data.SqlClient.dll");
        if (!File.Exists(libPath) || !File.Exists(refPath))
            return true;

        var selected = NuGetPackageAssetResolver.SelectPreferredCompileAssemblies([libPath, refPath]);
        return selected.Count == 1 && selected[0].Equals(refPath, StringComparison.Ordinal);
    }
}
