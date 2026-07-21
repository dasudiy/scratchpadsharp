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
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Isolation;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public interface IScriptExecutionService
{
    Task<ScriptExecutionResult> ExecuteAsync(string code, ProjectContext context, IDumpSink sink, CancellationToken ct = default);
}

public class ScriptExecutionService : IScriptExecutionService
{
    public async Task<ScriptExecutionResult> ExecuteAsync(string code, ProjectContext context, IDumpSink sink, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();

                var compilation = CompileScriptAsync(code, context);
                if (compilation.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var errors = compilation.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .ToList();

                    var errorText = string.Join(Environment.NewLine, errors.Select(d => d.ToString()));

                    // Prefer diagnostics mapped to user Script.cs; fall back to all errors.
                    var userDiagnostics = errors
                        .Where(d => d.Location.GetMappedLineSpan().Path == "Script.cs")
                        .ToList();

                    var diagnosticsToShow = userDiagnostics.Count > 0 ? userDiagnostics : errors;

                    var errorRecords = diagnosticsToShow.Select(d =>
                    {
                        var lineSpan = d.Location.GetMappedLineSpan();
                        return new CompilationError(
                            d.Id,
                            d.GetMessage(),
                            lineSpan.StartLinePosition.Line + 1,
                            lineSpan.StartLinePosition.Character + 1,
                            lineSpan.EndLinePosition.Line + 1,
                            lineSpan.EndLinePosition.Character + 1
                        );
                    }).ToList();

                    sink.ResultWrite(errorRecords);

                    return new ScriptExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Compilation failed",
                        Output = errorText,
                        CompilationErrors = errorRecords
                    };
                }

                ct.ThrowIfCancellationRequested();

                return await ExecuteInIsolationAsync(
                    compilation.Assembly,
                    context.Config,
                    sink,
                    ct,
                    context.AbsoluteNativeAssets,
                    context.AbsoluteCompileReferences);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return FailExecution(sink, "Script execution was cancelled", output: "Execution cancelled");
        }
        catch (Exception ex)
        {
            return FailExecution(sink, ex.Message, ex);
        }
    }



    private static (MemoryStream Assembly, string EntryPoint, List<Diagnostic> Diagnostics) CompileScriptAsync(
        string code, ProjectContext context)
    {
        var scriptDocument = ScriptDocumentBuilder.Build(code, context.Config.Usings);
        var syntaxTree = CSharpSyntaxTree.ParseText(scriptDocument.FullText);

        // Get reference assemblies from config and NuGet packages
        var references = MetadataReferenceProvider.GetReferencesWithPackages(context.AbsoluteCompileReferences).ToList();

        var compilation = CSharpCompilation.Create(
            $"__ScriptAssembly_{Guid.NewGuid():N}",
            [syntaxTree],
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
        MemoryStream assemblyStream,
        ScriptConfig config,
        IDumpSink sink,
        CancellationToken ct,
        List<string>? nativePaths = null,
        List<string>? compileReferences = null)
    {
        ScriptAssemblyLoadContext? alc = null;
        WeakReference? alcWeakRef = null;

        try
        {
            DumpExtension.UseSink(sink);

            var additionalPaths = new List<string>();

            if (nativePaths != null)
                additionalPaths.AddRange(nativePaths);

            var nugetPackagesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
            if (Directory.Exists(nugetPackagesPath))
                additionalPaths.Add(nugetPackagesPath);

            alc = new ScriptAssemblyLoadContext(null, additionalPaths, compileReferences);
            alcWeakRef = new WeakReference(alc);

            var assembly = alc.LoadFromStream(assemblyStream);

            var type = assembly.GetType("__ScriptRunner");
            if (type == null)
                return FailExecution(sink, "Could not find script runner type");

            var method = type.GetMethod("__Execute", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return FailExecution(sink, "Could not find script entry point");

            var connectionStringProp = type.GetProperty("__ConnectionString", BindingFlags.Public | BindingFlags.Static);
            connectionStringProp?.SetValue(null, config.ConnectionString);

            using var outputWriter = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;

            using var realTimeWriter = new RealTimeConsoleWriter(outputWriter, text =>
            {
                if (!string.IsNullOrEmpty(text))
                    sink.ResultWrite(text);
            });

            try
            {
                Console.SetOut(realTimeWriter);
                Console.SetError(realTimeWriter);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var executeTask = method.Invoke(null, null) as Task<object?>;
                if (executeTask == null)
                    return FailExecution(sink, "Method invocation failed");

                await executeTask.WaitAsync(linkedCts.Token);
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
            if (ct.IsCancellationRequested)
            {
                return FailExecution(sink, "Script execution was cancelled", output: "Execution cancelled");
            }

            return FailExecution(
                sink,
                $"Script execution timed out after {config.TimeoutSeconds} seconds",
                output: "Execution timeout");
        }
        catch (Exception ex)
        {
            var displayEx = ex.InnerException ?? ex;
            return FailExecution(sink, displayEx.Message, displayEx, displayEx.ToString());
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
                        const int monitorDurationMs = 10_000;
                        const int pollIntervalMs = 500;
                        var deadline = Environment.TickCount64 + monitorDurationMs;

                        while (alcWeakRef.IsAlive && Environment.TickCount64 < deadline)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                            await Task.Delay(pollIntervalMs);
                        }

                        if (!alcWeakRef.IsAlive)
                        {
                            Console.WriteLine("[ALC] Successfully collected");
                        }
                        else
                        {
                            Console.WriteLine("[ALC] Warning: Not collected after 10 seconds");
                        }
                    });
                }
            }

            assemblyStream?.Dispose();
        }
    }

    private static ScriptExecutionResult FailExecution(
        IDumpSink sink,
        string errorMessage,
        Exception? exception = null,
        string? output = null)
    {
        var displayException = exception ?? new Exception(errorMessage);
        sink.ResultWrite(displayException, new DumpOptions { Title = "Error" });

        return new ScriptExecutionResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Output = output ?? string.Empty,
            Exception = exception
        };
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
