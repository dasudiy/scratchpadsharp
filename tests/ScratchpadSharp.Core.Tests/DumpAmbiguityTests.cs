using System;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Core.Tests;

public static class DumpAmbiguityTests
{
    public static int RunAll()
    {
        AppConfiguration.Initialize();
        var failures = 0;
        failures += Run(nameof(Normalize_DropsDumpifyUsing), Normalize_DropsDumpifyUsing);
        failures += Run(nameof(Dump_WithTitle_PrefersNetPadWhenDumpifyImportedAsync),
            () => Dump_WithTitle_PrefersNetPadWhenDumpifyImportedAsync().GetAwaiter().GetResult());
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool Normalize_DropsDumpifyUsing()
    {
        var doc = ScriptDocumentBuilder.Build(
            """
            "hello".Dump("greet");
            """,
            ["System", "Dumpify", "ScratchpadSharp.Core.External.NetPad.Presentation"]);
        return !doc.EffectiveUsings.Contains("Dumpify") &&
               doc.EffectiveUsings.Contains("ScratchpadSharp.Core.External.NetPad.Presentation") &&
               !doc.FullText.Contains("using Dumpify;");
    }

    /// <summary>
    /// Reproduces CS0121 when both NetPad Dump and Dumpify Dump are imported
    /// and the script calls .Dump("title"), as EF Core query dumps do.
    /// </summary>
    private static async Task<bool> Dump_WithTitle_PrefersNetPadWhenDumpifyImportedAsync()
    {
        var tabId = Guid.NewGuid().ToString("N");
        var context = await ProjectService.Instance.CreateShellProjectAsync(tabId);
        context.Config.Usings =
        [
            "System",
            "ScratchpadSharp.Core.External.NetPad.Presentation",
            "Dumpify"
        ];
        context.MergedEnvironment.Usings = [..context.Config.Usings];

        var sink = new CollectDumpSink();
        var result = await new ScriptExecutionService().ExecuteAsync(
            """
            "hello".Dump("greet");
            """,
            context,
            sink);

        RoslynWorkspaceService.Instance.RemoveProject(tabId);

        if (!result.Success)
        {
            Console.WriteLine($"Execution failed: {result.ErrorMessage}\n{result.Output}");
            return false;
        }

        return sink.Dumped;
    }

    private sealed class CollectDumpSink : IDumpSink
    {
        public bool Dumped { get; private set; }

        public void ResultWrite<T>(T? value, DumpOptions? options = null)
        {
            if (Equals(value, "hello") && options?.Title == "greet")
                Dumped = true;
        }

        public void SqlWrite<T>(T? value, DumpOptions? options = null) { }
    }
}
