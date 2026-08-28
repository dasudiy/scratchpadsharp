# ScratchpadSharp - Technical Specification

**Project**: High-Performance C# Script Runner  
**Target Platform**: Linux (cross-platform capable)  
**Framework**: .NET 8.0 LTS  
**Last Updated**: June 6, 2026

---

## 1. Overview

ScratchpadSharp is a lightweight, high-performance C# scratchpad application built with Avalonia UI and Roslyn. It prioritizes startup speed, code execution isolation, and developer experience.

### Core Features

- **Fast Script Execution**: Roslyn-based C# compilation and execution
- **Memory Isolation**: AssemblyLoadContext with unloading to prevent memory leaks
- **IntelliSense**: Code completion, signature help, and formatting (Roslyn workspace)
- **Rich Object Visualization**: NetPad/O2Html HTML dumping via `.Dump()` (Dumpify as console fallback)
- **NuGet Support**: Dynamic package resolution via `ProjectService` / `DependencyResolver`
- **Git-Friendly Storage**: `.lqpkg` zip format with Developer Mode folder layout

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
├── SPECIFICATION.md
└── docs/
    ├── dump-workflow.md
    ├── intellisense-workflow.md
    ├── method-signature-help-workflow.md
    └── reference-management.md
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

Serialized from `ScriptConfig`:

```json
{
  "Usings": [
    "System",
    "System.Linq",
    "ScratchpadSharp.Core.External.NetPad.Presentation"
  ],
  "References": [
    "System.Runtime",
    "libs/MyLib.dll"
  ],
  "NuGetPackages": {
    "Newtonsoft.Json": "13.0.3"
  },
  "ConnectionString": "",
  "TimeoutSeconds": 30
}
```

Resolved assembly paths are stored in `manifest.json` (`ResolvedState`) — see [docs/reference-management.md](docs/reference-management.md).

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

- Receive hydrated `ProjectContext` (compile references and native assets already resolved by `ProjectService`)
- Compile user code in-memory via `CSharpCompilation` (wrapped in `__ScriptRunner.__Execute()`)
- Create a fresh collectible `ScriptAssemblyLoadContext` per execution
- Redirect `Console` output to the results panel
- Register `DumpDispatcher` as the `DumpExtension` sink before execution
- Unload ALC and force GC after each run
- Return `ScriptExecutionResult`

Reference resolution is handled by `ProjectService` — see [docs/reference-management.md](docs/reference-management.md).

**Key Methods**:

```csharp
Task<ScriptExecutionResult> ExecuteAsync(string code, ProjectContext context, CancellationToken ct = default);
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

- Query package metadata and transitive dependencies from configured NuGet sources
- Download packages to the NuGet global packages folder (`~/.nuget/packages`)
- Extract compile-time DLLs (`ref/` / `lib/`) and native assets (`runtimes/`)

Dependency graph resolution and manifest persistence are handled by `DependencyResolver` and `ProjectService`.

**Key Methods**:

```csharp
Task<IEnumerable<SourcePackageDependencyInfo>> GetPackageDependenciesAsync(PackageIdentity package, NuGetFramework framework, CancellationToken ct);
Task<string> EnsurePackageDownloadedAsync(string packageId, string version, IProgress<PackageInstallProgress> progress, CancellationToken ct);
Task<PackageAssets> GetPackageAssetsAsync(string packageRootPath, NuGetFramework targetFramework);
```

### 4.4 PackageService

**Responsibilities**:

- Save/load .lqpkg files (zip format)
- Save/load developer mode (folder format)
- Auto-detect format
- Pack/unpack between formats

**Key Methods**:

```csharp
Task SaveAsync(ScriptPackage package, string path);  // .lqpkg zip or developer folder (auto-detect)
Task<ScriptPackage> LoadAsync(string path);
Task PackAsync(string folderPath, string zipPath);   // library API; no UI command yet
Task UnpackAsync(string zipPath, string folderPath); // library API; no UI command yet
```

**Implementation Notes**:

- Use `System.IO.Compression.ZipArchive`
- UTF-8 without BOM for text entries
- Forward slashes in zip entry paths
- Atomic saves: write to .tmp file, then move
- `CompressionLevel.Optimal` for text files

---

## 5. Rich Object Visualization (NetPad/O2Html)

### 5.1 Overview

ScratchpadSharp utilizes a ported version of **NetPad**'s presentation layer, powered by **O2Html**, to provide rich, interactive object visualization similar to LINQPad.

**Features**:

- **HTML-Based**: Objects are serialized to structured HTML.
- **Interactive**: Collapsible trees for complex objects/collections.
- **Cyclic Reference Handling**: Gracefully handles circular dependencies.
- **Memory Safe**: Designed to work with `AssemblyLoadContext` unloading (no static leaks).

### 5.2 Integration Architecture

**In Roslyn Scripts**:

```csharp
// Users write:
var data = new { Name = "Test", Value = 42 };
data.Dump(); // Extension method from ScratchpadSharp.Core.External.NetPad.Presentation

var users = GetUsers();
users.Dump("User List");
```

**Under the Hood**:

1. **Compilation**: `ScriptExecutionService` registers `DumpExtension.UseSink(new DumpDispatcher())` and adds the NetPad presentation namespace via `ScriptConfig.Usings`.
2. **User call**: `DumpExtension.Dump()` invokes `IDumpSink.ResultWrite`.
3. **Serialization**: `DumpDispatcher` serializes via `HtmlPresenter` (O2Html) to an HTML string.
4. **Rendering**: `HtmlDumpService` wraps the HTML with embedded NetPad CSS and updates the UI via `Avalonia.HtmlRenderer`.

See [docs/dump-workflow.md](docs/dump-workflow.md) for a detailed flow.

### 5.3 Future Enhancement: WebView

**Goal**: Replace `Avalonia.HtmlRenderer` with a full `WebView` (e.g., CefGlue) for complete CSS/JS support and collapsible-tree interactivity.

**Current State**:

- Objects are serialized to HTML via O2Html.
- HTML dumps render in `HtmlPanel` (`Avalonia.HtmlRenderer`); console output uses `SelectableTextBlock`.
- The results panel toggles between HTML and text views.
- Collapsible tree expansion in dumps is limited without a full browser engine.

---

## 6. UI Design

### 6.1 MainWindow Layout

```xml
<Grid RowDefinitions="*, Auto, 2*">
    <!-- Code Editor -->
    <avaloniaEdit:TextEditor Grid.Row="0" ... />

    <!-- Splitter -->
    <GridSplitter Grid.Row="1" Height="4" />

    <!-- Results Panel (HTML dump + text console, toggle) -->
    <Panel Grid.Row="2">
        <ScrollViewer IsVisible="{Binding ShowHtmlOutput}">
            <the:HtmlPanel Text="{Binding HtmlOutput}" />
        </ScrollViewer>
        <ScrollViewer IsVisible="{Binding !ShowHtmlOutput}">
            <SelectableTextBlock Text="{Binding Output}" ... />
        </ScrollViewer>
    </Panel>
</Grid>
```

### 6.2 Menu Structure

Current implementation (see `MainWindow.axaml`):

```
File
├── New (Ctrl+N)
├── Open (Ctrl+O)
├── Save (Ctrl+S)
├── Save As (Ctrl+Shift+S)
└── Exit

Edit
├── Format Document (Ctrl+Alt+F)
└── Manage References (F4)

Run
├── Execute Script (F5)
└── Cancel (Shift+F5)
```

Planned (not yet in UI): Pack to Zip, Unpack to Folder, Settings, Developer Mode toggle.

### 6.3 ViewModel Structure

```csharp
public class MainWindowViewModel : ViewModelBase
{
    public string CodeText { get; set; }
    public string Output { get; set; }           // console text
    public string HtmlOutput { get; }           // from HtmlDumpService
    public bool ShowHtmlOutput { get; set; }
    public bool IsExecuting { get; set; }
    public bool IsProjectReady { get; }
    public ProjectContext ProjectContext { get; }

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    // ...

    private async Task ExecuteAsync()
    {
        var result = await scriptService.ExecuteAsync(CodeText, ProjectContext);
        Output = result.Output;
    }
}
```

### 6.4 Session lifecycle

When `Application.RestoreSessionOnStartup` is `true` (default):

- **On window close**: `MainWindowViewModel.SaveSession()` writes all tabs to `{LocalApplicationData}/ScratchpadSharp/session.json` — including unsaved tabs (no `SourcePath`), unsaved code edits, and reference state (`Config` + `Manifest`).
- **On startup**: `RestoreSessionAsync()` recreates tabs from that file instead of opening a single empty tab.

Unsaved tabs depend entirely on session data; saved `.lqpkg` tabs with Local zip entries require `PrepareEffectiveRootForSessionRestoreAsync` so `EffectiveRootPath` matches the package extract directory. `SaveAsZipAsync` does not currently pack local DLLs. Details: [docs/session-restore.md](docs/session-restore.md) and [docs/reference-management.md](docs/reference-management.md).

---

## 7. Configuration

### 7.1 appsettings.json

```json
{
  "Application": {
    "DeveloperMode": false,
    "RestoreSessionOnStartup": true,
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
```

| Key | Default | Description |
|-----|---------|-------------|
| `Application.RestoreSessionOnStartup` | `true` | Persist and restore open tabs, editor code, and references on exit/launch |
| `Application.DeveloperMode` | `false` | Folder-based package layout (reserved) |
| `Application.RecentFiles` | `[]` | Reserved; session restore uses `session.json` instead |

Session data path: `{LocalApplicationData}/ScratchpadSharp/session.json`. See [docs/session-restore.md](docs/session-restore.md).

> **Note**: `PackageCacheFolder` is reserved in appsettings; runtime package downloads use the NuGet global packages folder via `NuGetService`.
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

- Parse Roslyn `Diagnostic` objects and map to user `Script.cs` line numbers
- Display compilation errors via `.Dump()` in the HTML results panel
- Dedicated error panel and editor line highlighting — pending

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
- Limit serialization depth in O2Html / `HtmlPresenter`
- Periodic cleanup of temp files (zip local-asset extraction)

---

## 10. Implementation Phases

Status reflects the current codebase (see `README.md` for the detailed checklist).

### Phase 1: MVP — Complete

- Project structure, Avalonia UI, script execution, console redirection, save/load

### Phase 2: Isolation & Storage — Mostly complete

- AssemblyLoadContext, `.lqpkg` zip format — done
- Developer Mode folder layout and `PackAsync`/`UnpackAsync` — implemented in `PackageService`; UI commands pending

### Phase 3: NuGet & Object Visualization — Complete

- NuGet resolution via `ProjectService`, metadata references, NetPad/O2Html `.Dump()`, `config.json`

### Phase 4: EF Core & Polish — In progress

- Connection string via `ScriptConfig` — done
- EF Core integration, error highlighting, ANSI colors, Settings UI — pending

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

### 12.1 IntelliSense — Implemented

- Shared Roslyn workspace (`RoslynWorkspaceService`) with per-tab projects
- Code completion — see [docs/intellisense-workflow.md](docs/intellisense-workflow.md)
- Signature help — see [docs/method-signature-help-workflow.md](docs/method-signature-help-workflow.md)
- Code formatting (Ctrl+Alt+F)

**Future**: auto-import namespaces, richer completion providers

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
public async Task<ScriptExecutionResult> ExecuteAsync(string code, ProjectContext context, CancellationToken ct)
{
    return await Task.Run(async () =>
    {
        var compilation = CompileScript(code, context); // CSharpCompilation + in-memory emit
        if (compilation.HasErrors) { /* map diagnostics, Dump compilation errors */ }

        var alc = new ScriptAssemblyLoadContext(extraNativePaths: context.AbsoluteNativeAssets);
        WeakReference alcRef = new(alc);

        try
        {
            DumpExtension.UseSink(new DumpDispatcher());
            return await ExecuteInIsolationAsync(compilation.Assembly, compilation.EntryPoint, context.Config, context.AbsoluteNativeAssets);
        }
        finally
        {
            alc.Unload();
            for (int i = 0; i < 10 && alcRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }, ct);
}
```

Reference hydration and Roslyn metadata are handled by `ProjectService` before execution.

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

### 15.1 Workflow Documentation

- [docs/reference-management.md](docs/reference-management.md) — NuGet and assembly reference pipeline
- [docs/dump-workflow.md](docs/dump-workflow.md) — `.Dump()` HTML output flow
- [docs/intellisense-workflow.md](docs/intellisense-workflow.md) — Code completion pipeline
- [docs/method-signature-help-workflow.md](docs/method-signature-help-workflow.md) — Signature help pipeline

### 15.2 Useful Links

- Avalonia UI: [https://avaloniaui.net/](https://avaloniaui.net/)
- Roslyn Scripting API: [https://github.com/dotnet/roslyn/wiki/Scripting-API-Samples](https://github.com/dotnet/roslyn/wiki/Scripting-API-Samples)
- Dumpify: [https://github.com/MoaidHathot/Dumpify](https://github.com/MoaidHathot/Dumpify)
- NuGet.Protocol: [https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk)

### 15.3 License

[MIT License](LICENSE)

### 15.4 Contributors

- dasudiy

---

**End of Specification**