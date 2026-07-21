using System;
using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ScriptExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public object? ReturnValue { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public IReadOnlyList<CompilationError> CompilationErrors { get; set; } = Array.Empty<CompilationError>();
}

/// <summary>User-code compilation diagnostic mapped to Script.cs line/column (1-based).</summary>
public record CompilationError(
    string Id,
    string Message,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);
