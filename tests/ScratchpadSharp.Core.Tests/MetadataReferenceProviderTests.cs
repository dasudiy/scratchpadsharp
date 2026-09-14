using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Core.Tests;

internal static class MetadataReferenceProviderTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(DefaultReferences_IncludeCoreBcl), DefaultReferences_IncludeCoreBcl);
        failures += Run(nameof(GetReferencesWithPackages_EmptyExtras_StillIncludeBcl), GetReferencesWithPackages_EmptyExtras_StillIncludeBcl);
        failures += Run(nameof(GetReferencesWithPackages_CanCompileBasicScript), GetReferencesWithPackages_CanCompileBasicScript);
        failures += Run(nameof(GetReferencesWithPackages_IgnoresNativeDlls), GetReferencesWithPackages_IgnoresNativeDlls);
        return failures;
    }

    private static int Run(string name, Func<bool> test)
    {
        try
        {
            if (!test())
            {
                Console.WriteLine($"FAIL: {name}");
                return 1;
            }

            Console.WriteLine($"PASS: {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {name}: {ex}");
            return 1;
        }
    }

    private static bool DefaultReferences_IncludeCoreBcl()
    {
        var references = MetadataReferenceProvider.GetDefaultReferences().ToList();
        if (references.Count < 50)
            return Fail($"expected at least 50 framework references, got {references.Count}");

        return HasAssembly(references, "System.Runtime") && HasAssembly(references, "System.Private.CoreLib");
    }

    private static bool GetReferencesWithPackages_EmptyExtras_StillIncludeBcl()
    {
        var references = MetadataReferenceProvider.GetReferencesWithPackages([]).ToList();
        if (references.Count < 50)
            return Fail($"expected at least 50 merged references, got {references.Count}");

        return HasAssembly(references, "System.Runtime");
    }

    private static bool GetReferencesWithPackages_IgnoresNativeDlls()
    {
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var nativePath = Directory.EnumerateFiles(runtimeDir, "*.dll")
            .FirstOrDefault(path => !MetadataReferenceProvider.IsManagedAssembly(path));
        if (nativePath == null)
            return true;

        var references = MetadataReferenceProvider.GetReferencesWithPackages([nativePath]).ToList();
        var names = references
            .Select(r => Path.GetFileNameWithoutExtension(
                r is PortableExecutableReference portable && !string.IsNullOrWhiteSpace(portable.FilePath)
                    ? portable.FilePath
                    : r.Display))
            .ToList();

        if (names.Contains(Path.GetFileNameWithoutExtension(nativePath), StringComparer.OrdinalIgnoreCase))
            return Fail($"native dll was included: {nativePath}");

        return true;
    }

    private static bool GetReferencesWithPackages_CanCompileBasicScript()
    {
        var references = MetadataReferenceProvider.GetReferencesWithPackages([]).ToList();
        var tree = CSharpSyntaxTree.ParseText(
            """
            public static class Script
            {
                public static void Main() => System.Console.WriteLine("ok");
            }
            """,
            path: "Script.cs");

        var compilation = CSharpCompilation.Create(
            "MetadataReferenceProviderTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        if (errors.Count == 0)
            return true;

        return Fail(string.Join(Environment.NewLine, errors));
    }

    private static bool HasAssembly(IEnumerable<MetadataReference> references, string assemblyName) =>
        references.Any(r =>
            string.Equals(
                Path.GetFileNameWithoutExtension(
                    r is PortableExecutableReference portable && !string.IsNullOrWhiteSpace(portable.FilePath)
                        ? portable.FilePath
                        : r.Display),
                assemblyName,
                StringComparison.OrdinalIgnoreCase));

    private static bool Fail(string message)
    {
        Console.WriteLine(message);
        return false;
    }
}
