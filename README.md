# ScratchpadSharp

A lightweight, high-performance C# script runner built with Avalonia UI and Roslyn.

## Features

- **Fast Script Execution**: Roslyn-based C# compilation
- **Memory Isolation**: AssemblyLoadContext with unloading
- **IntelliSense Support**: Code completion, signature help, and formatting
- **Multi-Tab Editing**: Independent Roslyn project per tab
- **Rich Object Visualization**: HTML-based dumping (NetPad/O2Html)
- **NuGet Support**: Dynamic package resolution
- **Git-Friendly Storage**: .lqpkg zip format with Developer Mode folder layout
- **Session Restore**: Reopen tabs, unsaved code, and references after restart (configurable)

## Project Structure

```
src/
├── ScratchpadSharp/          # Avalonia UI application
├── ScratchpadSharp.Core/     # Script execution and storage
└── ScratchpadSharp.Shared/   # Shared models and exceptions
```

## Build & Run

```bash
dotnet build
dotnet run --project src/ScratchpadSharp/ScratchpadSharp.csproj
```

### GNOME Desktop Icon

Window/taskbar icons load from embedded assets. To appear in the **GNOME app grid**, install the freedesktop entry once:

```bash
dotnet build -c Release
chmod +x scripts/install-desktop-entry.sh
./scripts/install-desktop-entry.sh
```

This installs `~/.local/share/applications/scratchpad-sharp.desktop` and hicolor icons. Re-log or search "ScratchpadSharp" in Activities.

To point at a specific binary:

```bash
./scripts/install-desktop-entry.sh /path/to/ScratchpadSharp
```

## Development

### Phase 1: MVP ✓ Complete
- [x] Project structure and dependencies
- [x] Basic Avalonia UI with AvaloniaEdit
- [x] Simple script execution (no isolation)
- [x] Console output redirection
- [x] Save/load .lqpkg files

### Phase 2: Isolation & Storage ✓ Complete
- [x] AssemblyLoadContext implementation with isCollectible
- [x] ALC unloading with WeakReference monitoring
- [x] Native library resolver (Linux .so support)
- [x] In-memory compilation using CSharpCompilation
- [x] Isolated script execution with timeout support
- [x] Developer Mode folder layout (Open Folder / Save Folder UI + `PackageService`)
- [x] Pack/unpack (toolbar Pack / Unpack wired to `PackAsync`/`UnpackAsync`)

### Phase 2.5: Roslyn IntelliSense ✓ Complete
- [x] Shared workspace architecture (single AdhocWorkspace)
- [x] Code completion (Ctrl+Space, auto-trigger)
- [x] Signature help with XML documentation
- [x] Code formatting (Ctrl+Alt+F)
- [x] Multi-tab ready design (per-tab projects)
- [x] Thread-safe document updates
- [x] Async initialization with JIT warmup

### Phase 3: NuGet & Object Visualization ✓ Complete
- [x] MetadataReference management with XML docs
- [x] NuGet package resolution (via config.json)
- [x] Rich Object Dumping (NetPad/O2Html integration)
- [x] Memory leak prevention for Dumps
- [x] config.json support

### Phase 3.5: Multi-Tab & Session ✓ Complete
- [x] Multi-tab editing with per-tab Roslyn projects
- [x] Reference Management window (F4)
- [x] Session restore (tabs, unsaved code, references)

### Phase 4: EF Core & Polish
- [x] Connection string injection (via ScriptConfig)
- [x] Compilation error reporting (mapped to `Script.cs` line/column)
- [ ] EF Core integration
- [x] Editor error highlighting (wavy underlines from compilation diagnostics)
- [x] ANSI color support (console Text view renders SGR / Spectre sequences)
- [x] True execution cancellation (CancellationToken linked with timeout; Stop cancels wait)

### Phase 4.5: Layered Configuration ✓ Complete

Config resolves as **base → user → query**:

- [x] User settings layer `appsettings.user.json` in `{LocalApplicationData}/ScratchpadSharp/` (shipped `appsettings.json` stays read-only factory defaults; must live outside `bin`)
- [x] Global Settings UI editing the user layer, with hot-reload re-init of `ApplicationSettings` / `ConfigurationLoader` on `reloadOnChange`
- [x] Per-query Script settings in Reference Manager (F4): timeout / connection string → existing `config.json`
- [x] Inherited-vs-overridden value cues in the Script settings UI

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) — Technical design and architecture
- [docs/reference-management.md](docs/reference-management.md) — NuGet and assembly reference pipeline
- [docs/session-restore.md](docs/session-restore.md) — Session persistence (unsaved files, references, tabs)
- [docs/dump-workflow.md](docs/dump-workflow.md) — `.Dump()` HTML output flow
- [docs/intellisense-workflow.md](docs/intellisense-workflow.md) — Code completion pipeline
- [docs/method-signature-help-workflow.md](docs/method-signature-help-workflow.md) — Signature help pipeline

## Acknowledgements

Special thanks to [NetPad](https://github.com/tareqimbasher/NetPad) by Tareq Imbasher for the excellent HTML dumping implementation that ScratchpadSharp leverages.

## License

This project is licensed under the [MIT License](LICENSE).
