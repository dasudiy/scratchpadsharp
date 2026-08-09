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
}
