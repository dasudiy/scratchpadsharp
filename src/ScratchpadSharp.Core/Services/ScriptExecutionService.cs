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
                    var errors = string.Join(Environment.NewLine, 
                        compilation.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.ToString()));
                    
                    return new ScriptExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Compilation failed",
                        Output = errors
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

    private (MemoryStream Assembly, string EntryPoint, List<Diagnostic> Diagnostics) CompileScriptAsync(
        string code, ScriptConfig config)
    {
        // Wrap user code in a class with a static method
        var usingsBlock = string.Join(Environment.NewLine, config.DefaultUsings.Select(u => $"using {u};"));
        var wrappedCode = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
" + usingsBlock + @"

public class __ScriptRunner
{
    public static string __ConnectionString { get; set; } = string.Empty;

    public static async Task<object?> __Execute()
    {
        " + code + @"
        return null;
    }
}
";

        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

        // Get reference assemblies
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        };

        // Add System.Private.CoreLib and mscorlib
        try
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Private.CoreLib").Location));
        }
        catch { }

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

            // Redirect console output
            using var outputWriter = new StringWriter();
            var originalOut = Console.Out;

            try
            {
                Console.SetOut(outputWriter);

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
}

public class ScriptGlobals
{
    public string ConnectionString { get; set; } = string.Empty;
}
