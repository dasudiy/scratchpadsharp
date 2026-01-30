using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
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
                using var outputWriter = new StringWriter();
                var originalOut = Console.Out;

                try
                {
                    Console.SetOut(outputWriter);

                    var options = ScriptOptions.Default
                        .AddReferences(
                            typeof(System.Linq.Enumerable).Assembly,
                            typeof(System.Collections.Generic.List<>).Assembly,
                            typeof(System.Console).Assembly)
                        .AddImports(
                            "System",
                            "System.Linq",
                            "System.Collections.Generic",
                            "System.Threading.Tasks",
                            "System.IO")
                        .AddImports(config.DefaultUsings);

                    var globals = new ScriptGlobals
                    {
                        ConnectionString = config.ConnectionString
                    };

                    var result = await CSharpScript.RunAsync(
                        code,
                        options,
                        globals,
                        typeof(ScriptGlobals));

                    return new ScriptExecutionResult
                    {
                        Success = true,
                        Output = outputWriter.ToString(),
                        ReturnValue = result.ReturnValue
                    };
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
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
}

public class ScriptGlobals
{
    public string ConnectionString { get; set; } = string.Empty;
}
