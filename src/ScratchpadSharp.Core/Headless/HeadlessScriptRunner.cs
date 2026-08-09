using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Headless;

/// <summary>
/// Runs scripts without the Avalonia UI (for CI and local debugging).
/// </summary>
public static class HeadlessScriptRunner
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        AppConfiguration.Initialize();
        await RoslynWorkspaceService.Instance.EnsureInitializedAsync();

        if (args.Length == 0)
        {
            Console.Error.WriteLine(
                "Usage: ScratchpadSharp --headless run --module <instanceId> --code <csharp> | --file <path>");
            return 1;
        }

        if (!string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Unknown headless command. Use: run");
            return 1;
        }

        string? moduleId = null;
        string? code = null;
        string? file = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--module" when i + 1 < args.Length:
                    moduleId = args[++i];
                    break;
                case "--code" when i + 1 < args.Length:
                    code = args[++i];
                    break;
                case "--file" when i + 1 < args.Length:
                    file = args[++i];
                    break;
            }
        }

        if (file != null)
            code = await File.ReadAllTextAsync(file, ct);

        if (string.IsNullOrWhiteSpace(code))
        {
            Console.Error.WriteLine("Provide --code or --file");
            return 1;
        }

        var tabId = Guid.NewGuid().ToString("N");
        var context = await ProjectService.Instance.CreateShellProjectAsync(tabId, ct);
        context.Config.ModuleRefs = moduleId != null ? [moduleId] : [];
        context.Config.TimeoutSeconds = ApplicationSettings.DefaultTimeoutSeconds;

        await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId, context, ct);

        var sink = new TextDumpSink();
        var result = await new ScriptExecutionService().ExecuteAsync(code, context, sink, ct);

        if (!string.IsNullOrEmpty(sink.Text))
            Console.WriteLine(sink.Text);

        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage);
            if (!string.IsNullOrEmpty(result.Output))
                Console.Error.WriteLine(result.Output);
            return 1;
        }

        Console.WriteLine("OK");
        return 0;
    }

    private sealed class TextDumpSink : IDumpSink
    {
        public string Text { get; private set; } = string.Empty;

        public void ResultWrite<T>(T? value, DumpOptions? options = null)
        {
            if (value != null)
                Text += value.ToString() + Environment.NewLine;
        }

        public void SqlWrite<T>(T? value, DumpOptions? options = null) =>
            ResultWrite(value, options);
    }
}
