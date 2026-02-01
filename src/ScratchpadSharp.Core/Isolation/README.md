# Script Execution Isolation Implementation

## Overview
Implemented script execution isolation using a custom AssemblyLoadContext (ALC) with collectible assemblies for memory cleanup.

## Key Components

### 1. ScriptAssemblyLoadContext
Location: [src/ScratchpadSharp.Core/Isolation/ScriptAssemblyLoadContext.cs](src/ScratchpadSharp.Core/Isolation/ScriptAssemblyLoadContext.cs)

Features:
- **Collectible ALC**: Created with `isCollectible: true` for memory cleanup
- **AssemblyDependencyResolver**: Resolves managed assembly dependencies
- **Native Library Resolution**: Overrides `LoadUnmanagedDll` for Linux .so files
- **Runtime Identifier Detection**: Automatically detects platform (linux-x64, win-x64, osx-x64, etc.)
- **NuGet Runtime Probing**: Looks for native libraries in `runtimes/{rid}/native/` structure

### 2. ScriptExecutionService
Location: [src/ScratchpadSharp.Core/Services/ScriptExecutionService.cs](src/ScratchpadSharp.Core/Services/ScriptExecutionService.cs)

Key Changes:
- **Compilation Phase**: Uses `CSharpCompilation` instead of `CSharpScript.RunAsync`
  - Compiles user code into an in-memory assembly
  - Wraps code in a static class with async entry point
  - Provides detailed diagnostic errors
  
- **Isolation Phase**: Executes in a fresh ALC instance
  - Loads compiled assembly from memory stream
  - Finds and invokes the entry point via reflection
  - Provides connection string via static property
  
- **Console Redirection**: Captures `Console.WriteLine` output
  - Redirects `Console.Out` during execution
  - Returns captured output to caller
  
- **Cleanup Phase**: Unloads the ALC after execution
  - Calls `alc.Unload()` in finally block
  - Monitors GC collection using `WeakReference`
  - Logs collection status asynchronously
  
- **Timeout Support**: Uses `CancellationTokenSource` with configurable timeout

## Usage Example

```csharp
var config = new ScriptConfig
{
    DefaultUsings = new List<string> { "System.Text" },
    ConnectionString = "Server=localhost;Database=test;",
    TimeoutSeconds = 30
};

var code = @"
Console.WriteLine(""Hello from isolated script!"");
var numbers = Enumerable.Range(1, 10).ToList();
Console.WriteLine($""Sum: {numbers.Sum()}"");
";

var service = new ScriptExecutionService();
var result = await service.ExecuteAsync(code, config);

if (result.Success)
{
    Console.WriteLine(result.Output);
}
```

## Benefits

1. **Memory Safety**: Collectible ALCs can be garbage collected, preventing memory leaks
2. **Isolation**: Scripts run in isolated contexts, preventing interference
3. **Native Library Support**: Properly resolves EF Core and other native dependencies on Linux
4. **Timeout Protection**: Prevents runaway scripts from hanging the application
5. **Error Reporting**: Detailed compilation and runtime error messages

## Testing on Linux

The implementation properly handles:
- Linux-specific native libraries (.so files)
- NuGet package runtime folders structure
- EF Core native dependencies (e.g., `e_sqlite3`)

## Future Enhancements

1. Add support for loading additional NuGet packages at runtime
2. Implement assembly caching for repeated executions
3. Add resource limits (memory, CPU)
4. Support for debugging isolated scripts
