using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using ScratchpadSharp.Core.Isolation;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public interface IScriptExecutionService
{
    Task<ScriptExecutionResult> ExecuteAsync(string code, ScriptConfig config);
}

public class ScriptExecutionService : IScriptExecutionService
{
    public async Task<ScriptExecutionResult> ExecuteAsync(string code, ScriptConfig config)
    {
        try
        {
            return await Task.Run(async () =>
            {
                // Compile the script into an in-memory assembly
                var compilation = CompileScriptAsync(code, config);
                if (compilation.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var errors = compilation.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .ToList();

                    var errorText = string.Join(Environment.NewLine, errors.Select(d => d.ToString()));

                    // Format errors as HTML for the output pane
                    var htmlErrors = FormatCompilationErrors(errors);
                    DumpDispatcher.DispatchHtml(htmlErrors);

                    return new ScriptExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Compilation failed",
                        Output = errorText
                    };
                }

                // Execute in isolated ALC
                return await ExecuteInIsolationAsync(compilation.Assembly, compilation.EntryPoint, config);
            });
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Output = ex.ToString(),
                Exception = ex
            };
        }
    }

    private string FormatCompilationErrors(List<Diagnostic> diagnostics)
    {
        // Filter out errors that point to the wrapper code (empty path or not Script.cs)
        // unless we have no errors mapped to user code, in which case we show everything.
        var userDiagnostics = diagnostics
            .Where(d => d.Location.GetMappedLineSpan().Path == "Script.cs")
            .ToList();

        var diagnosticsToShow = userDiagnostics.Count > 0 ? userDiagnostics : diagnostics;

        var sb = new StringBuilder();
        sb.Append("<div class='group error'>");

        foreach (var diagnostic in diagnosticsToShow)
        {
            var lineSpan = diagnostic.Location.GetMappedLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;

            sb.Append("<div>");
            sb.Append(System.Web.HttpUtility.HtmlEncode(diagnostic.Id));
            sb.Append(": ");
            sb.Append(System.Web.HttpUtility.HtmlEncode(diagnostic.GetMessage()));
            sb.Append($" (Line {line}, Column {column})");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private (MemoryStream Assembly, string EntryPoint, List<Diagnostic> Diagnostics) CompileScriptAsync(
        string code, ScriptConfig config)
    {
        var preprocessor = new ScriptPreprocessor();
        var (cleanCode, userUsings, removedLineCount) = preprocessor.ExtractUsingsAndComments(code);

        var allUsings = config.DefaultUsings.Concat(userUsings).Distinct();
        var usingsBlock = string.Join(Environment.NewLine, allUsings.Select(u => $"using {u};"));

        var lineDirective = $"#line {removedLineCount + 1} \"Script.cs\"";
        var wrappedCode = usingsBlock + @"

public class __ScriptRunner
{
    public static string __ConnectionString { get; set; } = string.Empty;

    public static async Task<object?> __Execute()
    {
    " + lineDirective + @"
        " + cleanCode + @"
#line hidden
        return null;
    }
}

";

        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

        // Get reference assemblies from config and NuGet packages
        var references = MetadataReferenceProvider.GetReferencesFromConfig(config).ToList();

        var compilation = CSharpCompilation.Create(
            $"__ScriptAssembly_{Guid.NewGuid():N}",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false));

        var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);

        var diagnostics = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        assemblyStream.Seek(0, SeekOrigin.Begin);

        return (assemblyStream, "__ScriptRunner.__Execute", diagnostics);
    }

    private async Task<ScriptExecutionResult> ExecuteInIsolationAsync(
        MemoryStream assemblyStream, string entryPoint, ScriptConfig config)
    {
        ScriptAssemblyLoadContext? alc = null;
        WeakReference? alcWeakRef = null;

        try
        {
            // Set the Dump Sink to our custom one
            // This ensures .Dump() calls go through ScratchpadDumpSink -> HtmlPresenter -> DumpDispatcher
            ScratchpadSharp.Core.External.NetPad.Presentation.DumpExtension.UseSink(new DumpDispatcher());

            // Create isolated ALC with additional probing paths if needed
            var additionalPaths = new List<string>();

            // Add NuGet package paths if available
            var nugetPackagesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
            if (Directory.Exists(nugetPackagesPath))
            {
                additionalPaths.Add(nugetPackagesPath);
            }

            alc = new ScriptAssemblyLoadContext(null, additionalPaths);
            alcWeakRef = new WeakReference(alc);

            // Load assembly from memory
            var assembly = alc.LoadFromStream(assemblyStream);

            // Find the entry point
            var type = assembly.GetType("__ScriptRunner");
            if (type == null)
            {
                return new ScriptExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Could not find script runner type"
                };
            }

            var method = type.GetMethod("__Execute", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return new ScriptExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Could not find script entry point"
                };
            }

            // Set connection string
            var connectionStringProp = type.GetProperty("__ConnectionString", BindingFlags.Public | BindingFlags.Static);
            connectionStringProp?.SetValue(null, config.ConnectionString);

            // Redirect console output to capture Console.WriteLine
            using var outputWriter = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;

            // Create a custom writer that forwards to outputWriter AND DumpDispatcher immediately
            // This allows real-time output in the UI
            using var realTimeWriter = new RealTimeConsoleWriter(outputWriter, (text) =>
            {
                // Dispatch specialized text message, or just generic text
                // We wrap it in a span or div to differentiate from HTML dumps
                // For now, let's just dispatch it as text wrapped in pre
                if (!string.IsNullOrEmpty(text))
                {
                    DumpDispatcher.DispatchHtml($"<div class='text'>{System.Web.HttpUtility.HtmlEncode(text)}</div>");
                }
            });

            try
            {
                Console.SetOut(realTimeWriter);
                Console.SetError(realTimeWriter);

                // Execute with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
                var executeTask = method.Invoke(null, null) as Task<object?>;

                if (executeTask == null)
                {
                    return new ScriptExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Method invocation failed"
                    };
                }

                await executeTask.WaitAsync(cts.Token);
                var returnValue = await executeTask;

                return new ScriptExecutionResult
                {
                    Success = true,
                    Output = outputWriter.ToString(),
                    ReturnValue = returnValue
                };
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
        catch (OperationCanceledException)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ErrorMessage = $"Script execution timed out after {config.TimeoutSeconds} seconds",
                Output = "Execution timeout"
            };
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ErrorMessage = ex.InnerException?.Message ?? ex.Message,
                Output = (ex.InnerException ?? ex).ToString(),
                Exception = ex.InnerException ?? ex
            };
        }
        finally
        {
            // Cleanup: Unload the ALC
            if (alc != null)
            {
                alc.Unload();

                // Monitor GC collection (async fire-and-forget)
                if (alcWeakRef != null)
                {
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 10 && alcWeakRef.IsAlive; i++)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            await Task.Delay(100);
                        }

                        if (!alcWeakRef.IsAlive)
                        {
                            Console.WriteLine("[ALC] Successfully collected");
                        }
                        else
                        {
                            Console.WriteLine("[ALC] Warning: Not collected after 10 attempts");
                        }
                    });
                }
            }

            assemblyStream?.Dispose();
        }
    }

    // Helper class for real-time console redirection
    private class RealTimeConsoleWriter : StringWriter
    {
        private readonly StringWriter _backingWriter;
        private readonly Action<string> _onWrite;

        public RealTimeConsoleWriter(StringWriter backingWriter, Action<string> onWrite)
        {
            _backingWriter = backingWriter;
            _onWrite = onWrite;
        }

        public override void Write(char value)
        {
            _backingWriter.Write(value);
            _onWrite(value.ToString());
        }

        public override void Write(string? value)
        {
            _backingWriter.Write(value);
            if (value != null) _onWrite(value);
        }

        public override void WriteLine(string? value)
        {
            _backingWriter.WriteLine(value);
            if (value != null) _onWrite(value + Environment.NewLine);
        }

        public override Encoding Encoding => _backingWriter.Encoding;
    }
}

public class ScriptGlobals
{
    public string ConnectionString { get; set; } = string.Empty;
}
