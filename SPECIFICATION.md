# ScratchpadSharp - Technical Specification

**Project**: High-Performance C# Script Runner  
**Target Platform**: Linux (cross-platform capable)  
**Framework**: .NET 8.0 LTS  
**Last Updated**: January 30, 2026

---

## 1. Overview

ScratchpadSharp is a lightweight, high-performance C# scratchpad application built with Avalonia UI and Roslyn. It prioritizes startup speed, code execution isolation, and developer experience.

### Core Features
- **Fast Script Execution**: Roslyn-based C# compilation and execution
- **Memory Isolation**: AssemblyLoadContext with unloading to prevent memory leaks
- **Rich Object Visualization**: Dumpify integration for formatted output
- **NuGet Support**: Dynamic package resolution and loading
- **EF Core Integration**: Built-in support for database queries
- **Git-Friendly Storage**: .lqpkg format with Developer Mode

---

## 2. Architecture

### 2.1 Project Structure

```
scratchpad-sharp/
├── ScratchpadSharp.sln
├── src/
│   ├── ScratchpadSharp/                    # Main Avalonia UI project
│   │   ├── ScratchpadSharp.csproj
│   │   ├── Program.cs
│   │   ├── App.axaml / App.axaml.cs
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   └── ViewModelBase.cs
│   │   ├── Views/
│   │   │   └── MainWindow.axaml
│   │   └── appsettings.json
│   │
│   ├── ScratchpadSharp.Core/               # Core business logic
│   │   ├── ScratchpadSharp.Core.csproj
│   │   ├── Services/
│   │   │   └── ScriptExecutionService.cs
│   │   ├── Isolation/
│   │   │   └── ScriptAssemblyLoadContext.cs
│   │   ├── PackageManagement/
│   │   │   └── NuGetService.cs
│   │   ├── Storage/
│   │   │   └── PackageService.cs
│   │   └── Configuration/
│   │       └── ConfigurationLoader.cs
│   │
│   └── ScratchpadSharp.Shared/             # Shared models
│       ├── ScratchpadSharp.Shared.csproj
│       ├── Models/
│       │   ├── ScriptPackage.cs
│       │   ├── PackageManifest.cs
│       │   └── ScriptExecutionResult.cs
│       └── Exceptions/
│           └── PackageException.cs
│
└── docs/
    └── SPECIFICATION.md (this file)
```

### 2.2 Key Dependencies

#### Main UI Project
```xml
<PackageReference Include="Avalonia" Version="11.0.*" />
<PackageReference Include="Avalonia.Desktop" Version="11.0.*" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.0.*" />
<PackageReference Include="AvaloniaEdit" Version="11.0.*" />
<PackageReference Include="Avalonia.ReactiveUI" Version="11.0.*" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.*" />
```

#### Core Library
```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.11.*" />
<PackageReference Include="NuGet.Protocol" Version="6.8.*" />
<PackageReference Include="NuGet.Packaging" Version="6.8.*" />
<PackageReference Include="Dumpify" Version="0.6.*" />
```

---

## 3. File Format: .lqpkg

### 3.1 Structure (Zip Mode)

```
package.lqpkg
├── manifest.json       # Package metadata and format version
├── code.cs             # C# script content
├── config.json         # NuGet packages and connection strings
└── last_run.txt        # (Optional) Last execution output
```

### 3.2 manifest.json Schema

```json
{
  "formatVersion": "1.0",
  "created": "2026-01-30T10:00:00Z",
  "modified": "2026-01-30T12:00:00Z",
  "metadata": {
    "name": "Script Name",
    "description": "Script description",
    "author": "username",
    "tags": ["demo", "ef-core"]
  }
}
```

### 3.3 config.json Schema

```json
{
  "nuget": {
    "sources": [
      "https://api.nuget.org/v3/index.json"
    ],
    "packages": [
      {
        "id": "Newtonsoft.Json",
        "version": "13.0.3"
      },
      {
        "id": "Microsoft.EntityFrameworkCore.SqlServer",
        "version": "8.0.0"
      }
    ]
  },
  "database": {
    "connectionString": "Server=localhost;Database=Test;Integrated Security=true;"
  },
  "execution": {
    "timeoutSeconds": 30,
    "maxMemoryMb": 512
  }
}
```

### 3.4 Developer Mode (Folder Structure)

```
MyPackage/
├── .lqpkg/
│   └── manifest.json       # Hidden metadata folder
├── code.cs                 # Script content (git-friendly)
├── config.json             # Configuration (git-friendly)
└── last_run.txt            # (Optional) Output
```

**Benefits**:
- Git-friendly: text files with clear diffs
- Easy editing: no need to unzip
- Version control: track changes line-by-line

**Switching Modes**:
- Auto-detect: Check if path ends with `.lqpkg` (zip) or has `.lqpkg/manifest.json` (folder)
- Commands: Pack (folder → zip), Unpack (zip → folder)

---

## 4. Core Components

### 4.1 ScriptExecutionService

**Responsibilities**:
- Load config.json and resolve NuGet packages
- Add MetadataReferences (Dumpify, Spectre.Console, EF Core, NuGet packages)
- Create fresh AssemblyLoadContext
- Compile code using Roslyn
- Execute script with ScriptGlobals (ConnectionString, etc.)
- Capture Dumpify output to StringWriter
- Unload ALC and force GC
- Return execution result

**Key Methods**:
```csharp
Task<ScriptExecutionResult> ExecuteAsync(string code, ScriptConfig config);
Task<List<MetadataReference>> ResolveReferencesAsync(ScriptConfig config);
```

### 4.2 ScriptAssemblyLoadContext

**Configuration**:
```csharp
public ScriptAssemblyLoadContext() : base(isCollectible: true)
{
    // Enable unloading for memory isolation
}
```

**Unloading Pattern**:
```csharp
WeakReference alcWeakRef;
{
    var alc = new ScriptAssemblyLoadContext();
    // Execute script
    alcWeakRef = new WeakReference(alc);
    alc.Unload();
}

for (int i = 0; i < 10 && alcWeakRef.IsAlive; i++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
```

### 4.3 NuGetService

**Responsibilities**:
- Read package dependencies from config.json
- Download packages to `./.packages` cache
- Resolve transitive dependencies
- Extract DLLs from `.nupkg` files
- Return list of assembly paths

**Key Methods**:
```csharp
Task<List<string>> ResolvePackageAssembliesAsync(PackageReference[] packages);
Task<string> DownloadPackageAsync(string packageId, string version);
```

### 4.4 PackageService

**Responsibilities**:
- Save/load .lqpkg files (zip format)
- Save/load developer mode (folder format)
- Auto-detect format
- Pack/unpack between formats

**Key Methods**:
```csharp
Task SaveAsync(ScriptPackage package, string path, bool developerMode);
Task<ScriptPackage> LoadAsync(string path);
Task PackAsync(string folderPath, string zipPath);
Task UnpackAsync(string zipPath, string folderPath);
```

**Implementation Notes**:
- Use `System.IO.Compression.ZipArchive`
- UTF-8 without BOM for text entries
- Forward slashes in zip entry paths
- Atomic saves: write to .tmp file, then move
- `CompressionLevel.Optimal` for text files

---

## 5. Dumpify Integration

### 5.1 Overview

**Dumpify** is a rich object visualization library built on Spectre.Console.

**Features Used**:
- Structured tables for collections
- Nested object trees
- Circular reference handling
- Max depth control
- Custom output redirection

### 5.2 Integration Approach

**In Roslyn Scripts**:
```csharp
// Users write:
var data = new { Name = "Test", Value = 42 };
data.Dump();

var users = GetUsers();
users.Dump(maxDepth: 2);
```

**Output Redirection**:
```csharp
// In ScriptExecutionService:
using var writer = new StringWriter();
var dumpOutput = new DumpOutput(writer);

// Configure Dumpify for plain text (no ANSI colors initially)
DumpConfig.Default.ColorConfig = ColorConfig.NoColors;

// Execute script with Dumpify available
var options = ScriptOptions.Default
    .AddReferences(typeof(DumpExtensions).Assembly)
    .AddReferences(typeof(Spectre.Console.AnsiConsole).Assembly)
    .AddImports("Dumpify");

// Capture output
string output = writer.ToString();
```

### 5.3 Future Enhancement: ANSI Color Support

**Goal**: Parse Dumpify's ANSI output and render in Avalonia with colors.

**Approach**:
1. Enable colors: `DumpConfig.Default.ColorConfig = ColorConfig.Default`
2. Capture ANSI output
3. Parse VT100 escape sequences (e.g., `\x1b[31m` = red)
4. Convert to Avalonia `TextBlock` with styled `Run` elements
5. Apply `Foreground`/`Background` brushes

**Alternative**: Use Avalonia terminal control for native ANSI rendering.

---

## 6. UI Design

### 6.1 MainWindow Layout

```xml
<Grid RowDefinitions="*, Auto, 2*">
    <!-- Code Editor -->
    <avaloniaEdit:TextEditor Grid.Row="0"
                             Document="{Binding CodeDocument}"
                             FontFamily="Cascadia Code"
                             FontSize="14"
                             ShowLineNumbers="True" />
    
    <!-- Splitter -->
    <GridSplitter Grid.Row="1" Height="4" />
    
    <!-- Results Panel -->
    <Border Grid.Row="2" Background="#1E1E1E">
        <SelectableTextBlock Text="{Binding Output}"
                             FontFamily="Cascadia Code"
                             FontSize="12"
                             Foreground="White" />
    </Border>
</Grid>
```

### 6.2 Menu Structure

```
File
├── New (Ctrl+N)
├── Open (Ctrl+O)
├── Save (Ctrl+S)
├── Save As (Ctrl+Shift+S)
├── ──────────
├── Pack to Zip
├── Unpack to Folder
├── ──────────
└── Exit

Edit
├── Undo (Ctrl+Z)
├── Redo (Ctrl+Y)
├── ──────────
├── Cut (Ctrl+X)
├── Copy (Ctrl+C)
└── Paste (Ctrl+V)

Run
├── Execute Script (F5)
└── Cancel Execution (Shift+F5)

Tools
├── Manage NuGet Packages
├── Settings
└── Toggle Developer Mode
```

### 6.3 ViewModel Structure

```csharp
public class MainWindowViewModel : ReactiveObject
{
    private string output;
    private bool isExecuting;
    private TextDocument codeDocument;
    
    public TextDocument CodeDocument { get; set; }
    public string Output { get; set; }
    public bool IsExecuting { get; set; }
    
    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadCommand { get; }
    
    private async Task ExecuteAsync()
    {
        IsExecuting = true;
        try
        {
            var result = await Task.Run(() => 
                scriptService.ExecuteAsync(CodeDocument.Text, currentConfig));
            
            Output = result.Output;
        }
        catch (Exception ex)
        {
            Output = $"Error: {ex.Message}\n{ex.StackTrace}";
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
```

---

## 7. Configuration

### 7.1 appsettings.json

```json
{
  "Application": {
    "DeveloperMode": false,
    "RecentFiles": []
  },
  "Editor": {
    "FontFamily": "Cascadia Code",
    "FontSize": 14,
    "ShowLineNumbers": true,
    "TabSize": 4
  },
  "Execution": {
    "DefaultTimeoutSeconds": 30,
    "MaxMemoryMb": 512
  },
  "NuGet": {
    "DefaultSources": [
      "https://api.nuget.org/v3/index.json"
    ],
    "PackageCacheFolder": "./.packages"
  },
  "DefaultUsings": [
    "System",
    "System.Linq",
    "System.Collections.Generic",
    "Dumpify",
    "Microsoft.EntityFrameworkCore"
  ]
}
```

---

## 8. Error Handling

### 8.1 Exception Hierarchy

```csharp
public class PackageException : Exception { }
public class CorruptPackageException : PackageException { }
public class UnsupportedFormatException : PackageException 
{
    public FormatVersion FileVersion { get; }
    public FormatVersion AppVersion { get; }
}

public class ScriptExecutionException : Exception { }
public class CompilationException : ScriptExecutionException 
{
    public Diagnostic[] Diagnostics { get; }
}
```

### 8.2 Error Display

**Compilation Errors**:
- Parse Roslyn `Diagnostic` objects
- Display in separate error panel
- Show line numbers and error codes
- Highlight error lines in editor

**Runtime Errors**:
- Catch exceptions during execution
- Format stack traces
- Show in output panel
- Preserve error context

---

## 9. Performance Considerations

### 9.1 Startup Speed
- No heavy DI frameworks (use vanilla DI)
- Lazy-load NuGet packages
- Minimize assembly loading at startup
- Use ReactiveUI (lightweight MVVM)

### 9.2 Execution Speed
- Compile scripts with `CompressionLevel.Optimal`
- Cache compiled scripts (future enhancement)
- Parallel NuGet package resolution
- Stream-based zip operations (avoid memory buffers)

### 9.3 Memory Management
- Unload ALC after each execution
- Force GC collection after unload
- Monitor with `WeakReference`
- Limit max depth in Dumpify
- Periodic cleanup of temp files

---

## 10. Implementation Phases

### Phase 1: MVP (Week 1-2)
- [ ] Project structure and dependencies
- [ ] Basic Avalonia UI with AvaloniaEdit
- [ ] Simple script execution (no isolation)
- [ ] Console output redirection
- [ ] Save/load plain .cs files

### Phase 2: Isolation & Storage (Week 3-4)
- [ ] AssemblyLoadContext implementation
- [ ] .lqpkg zip format
- [ ] Developer Mode (folder structure)
- [ ] Pack/unpack commands

### Phase 3: NuGet & Dumpify (Week 5-6)
- [ ] NuGet package resolution
- [ ] Dumpify integration
- [ ] MetadataReference management
- [ ] config.json support

### Phase 4: EF Core & Polish (Week 7-8)
- [ ] EF Core integration
- [ ] Connection string injection
- [ ] Error highlighting in editor
- [ ] ANSI color support (optional)
- [ ] Settings UI

---

## 11. Testing Strategy

### 11.1 Unit Tests
- ScriptExecutionService: compilation and execution
- NuGetService: package resolution
- PackageService: save/load operations
- ALC unloading verification

### 11.2 Integration Tests
- End-to-end script execution
- NuGet package loading
- File format compatibility
- Developer mode switching

### 11.3 Performance Tests
- Startup time benchmarks
- Memory leak detection
- Large script compilation
- Multiple executions in sequence

---

## 12. Future Enhancements

### 12.1 IntelliSense
- Use Roslyn completion providers
- Implement `ICompletionProvider` for AvaloniaEdit
- Show method signatures and documentation
- Auto-import namespaces

### 12.2 Debugging
- Breakpoint support
- Step-through debugging
- Variable inspection
- Call stack visualization

### 12.3 Script Templates
- Pre-configured templates
- EF Core query template
- API client template
- Data processing template

### 12.4 Export Features
- Export to .csproj
- Generate console app
- Create NuGet package
- Share as gist

---

## 13. Reference Implementation Patterns

### 13.1 Async Script Execution

```csharp
public async Task<ScriptExecutionResult> ExecuteAsync(string code, ScriptConfig config)
{
    return await Task.Run(async () =>
    {
        // Resolve packages
        var assemblies = await nuGetService.ResolveAsync(config.Packages);
        
        // Build options
        var options = ScriptOptions.Default
            .AddReferences(assemblies)
            .AddReferences(typeof(DumpExtensions).Assembly)
            .AddImports(config.DefaultUsings);
        
        // Create ALC
        var alc = new ScriptAssemblyLoadContext();
        WeakReference alcRef = new(alc);
        
        try
        {
            // Redirect output
            using var writer = new StringWriter();
            
            // Execute
            var globals = new ScriptGlobals 
            { 
                ConnectionString = config.ConnectionString 
            };
            
            var result = await CSharpScript.RunAsync(code, options, globals);
            
            return new ScriptExecutionResult
            {
                Success = true,
                Output = writer.ToString(),
                ReturnValue = result.ReturnValue
            };
        }
        finally
        {
            // Unload ALC
            alc.Unload();
            
            for (int i = 0; i < 10 && alcRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10);
            }
        }
    });
}
```

### 13.2 Package Save/Load

```csharp
public async Task SaveAsync(ScriptPackage package, string path)
{
    var tempPath = $"{path}.tmp";
    
    try
    {
        using var fileStream = File.Create(tempPath);
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            // Add manifest
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var stream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, package.Manifest,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            
            // Add code
            var codeEntry = archive.CreateEntry("code.cs");
            using (var stream = codeEntry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(package.Code);
            }
            
            // Add config
            var configEntry = archive.CreateEntry("config.json");
            using (var stream = configEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, package.Config,
                    new JsonSerializerOptions { WriteIndented = true });
            }
        }
        
        File.Move(tempPath, path, overwrite: true);
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
}
```

---

## 14. Security Considerations

### 14.1 Script Execution
- Scripts run with full trust (no sandbox)
- User responsibility to review code
- Warning on first execution
- No automatic script execution

### 14.2 NuGet Packages
- Only download from trusted sources
- Verify package signatures (future)
- Scan for known vulnerabilities (future)
- User approval for new packages

### 14.3 File System Access
- Scripts have full file system access
- No restrictions on file operations
- User should understand risks

---

## 15. Appendix

### 15.1 Useful Links
- Avalonia UI: https://avaloniaui.net/
- Roslyn Scripting API: https://github.com/dotnet/roslyn/wiki/Scripting-API-Samples
- Dumpify: https://github.com/MoaidHathot/Dumpify
- NuGet.Protocol: https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk

### 15.2 License
MIT License (TBD)

### 15.3 Contributors
- [Your Name]

---

**End of Specification**
