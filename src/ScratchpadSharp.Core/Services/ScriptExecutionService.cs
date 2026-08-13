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
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Isolation;
using ScratchpadSharp.Core.PackageManagement;
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

                await using var tunnels = await SshTunnelScope.OpenAsync(
                    context.MergedEnvironment.ResolvedModules, ct);
                var moduleSources = tunnels.RewriteSources(
                    context.MergedEnvironment.ResolvedModules,
                    context.MergedEnvironment.ModuleSources);

                var compilation = CompileScriptAsync(code, context, moduleSources);
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

                var runtimeReferences = context.AbsoluteRuntimeReferences.Count > 0
                    ? context.AbsoluteRuntimeReferences
                    : context.AbsoluteCompileReferences
                        .Select(NuGetPackageAssetResolver.ResolveRuntimeAssemblyPath)
                        .ToList();

                return await ExecuteInIsolationAsync(
                    compilation.Assembly,
                    context.Config,
                    sink,
                    ct,
                    context.AbsoluteNativeAssets,
                    runtimeReferences);
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
        string code, ProjectContext context, IReadOnlyList<ModuleSourceFile>? moduleSources = null)
    {
        var merged = context.MergedEnvironment;
        var usings = merged.Usings.Count > 0 ? merged.Usings : context.Config.Usings;
        var scriptDocument = ScriptDocumentBuilder.Build(code, usings);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = new List<SyntaxTree>();
        foreach (var module in moduleSources ?? merged.ModuleSources)
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(module.SourceText, parseOptions, path: module.FileName));

        syntaxTrees.Add(CSharpSyntaxTree.ParseText(scriptDocument.FullText, parseOptions, path: "Script.cs"));

        // Get reference assemblies from config and NuGet packages
        var references = MetadataReferenceProvider.GetReferencesWithPackages(context.AbsoluteCompileReferences).ToList();

        var compilation = CSharpCompilation.Create(
            $"__ScriptAssembly_{Guid.NewGuid():N}",
            syntaxTrees,
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
        List<string>? runtimeReferences = null)
    {
        ScriptAssemblyLoadContext? alc = null;
        Task<object?>? executeTask = null;

        try
        {
            DumpExtension.UseSink(sink);

            var additionalPaths = new List<string>();

            if (nativePaths != null)
                additionalPaths.AddRange(nativePaths);

            if (runtimeReferences != null)
            {
                foreach (var reference in runtimeReferences)
                {
                    var packageRoot = NuGetPackageAssetResolver.InferPackageRoot(reference);
                    if (packageRoot != null && !additionalPaths.Contains(packageRoot))
                        additionalPaths.Add(packageRoot);
                }
            }

            var nugetPackagesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
            if (Directory.Exists(nugetPackagesPath))
                additionalPaths.Add(nugetPackagesPath);

            alc = new ScriptAssemblyLoadContext(null, additionalPaths, runtimeReferences);

            var assembly = alc.LoadFromStream(assemblyStream);

            var type = assembly.GetType("__ScriptRunner");
            if (type == null)
                return FailExecution(sink, "Could not find script runner type");

            var method = type.GetMethod(
                "__Execute",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(CancellationToken)],
                modifiers: null);
            if (method == null)
                return FailExecution(sink, "Could not find script entry point");

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

                executeTask = method.Invoke(null, [linkedCts.Token]) as Task<object?>;
                if (executeTask == null)
                    return FailExecution(sink, "Method invocation failed");

                try
                {
                    await executeTask.WaitAsync(linkedCts.Token);
                    var returnValue = await executeTask;

                    return new ScriptExecutionResult
                    {
                        Success = true,
                        Output = outputWriter.ToString(),
                        ReturnValue = returnValue
                    };
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        await executeTask.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch
                    {
                        /* script ignored cancellationToken and is still running */
                    }

                    throw;
                }
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
            DumpExtension.ResetSink();
            if (executeTask is null or { IsCompleted: true })
                alc?.Unload();
            assemblyStream.Dispose();
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
        private readonly StringBuilder _pending = new();

        public RealTimeConsoleWriter(StringWriter backingWriter, Action<string> onWrite)
        {
            _backingWriter = backingWriter;
            _onWrite = onWrite;
        }

        public override void Write(char value)
        {
            _backingWriter.Write(value);
            _pending.Append(value);
            if (value == '\n')
                FlushPending();
        }

        public override void Write(string? value)
        {
            _backingWriter.Write(value);
            if (string.IsNullOrEmpty(value))
                return;
            _pending.Append(value);
            if (value.Contains('\n'))
                FlushPending();
        }

        public override void WriteLine(string? value)
        {
            _backingWriter.WriteLine(value);
            _pending.Append(value);
            _pending.Append(Environment.NewLine);
            FlushPending();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                FlushPending();
            base.Dispose(disposing);
        }

        private void FlushPending()
        {
            if (_pending.Length == 0)
                return;
            var text = _pending.ToString();
            _pending.Clear();
            _onWrite(text);
        }

        public override Encoding Encoding => _backingWriter.Encoding;
    }
}
