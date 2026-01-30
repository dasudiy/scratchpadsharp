using System;

namespace ScratchpadSharp.Shared.Models;

public class ScriptExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public object? ReturnValue { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
