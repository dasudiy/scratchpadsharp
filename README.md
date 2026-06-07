# ScratchpadSharp

A lightweight, high-performance C# script runner built with Avalonia UI and Roslyn.

## Features

- **Fast Script Execution**: Roslyn-based C# compilation
- **Memory Isolation**: AssemblyLoadContext with unloading
- **IntelliSense Support**: Code completion, signature help, and formatting
- **Rich Object Visualization**: HTML-based dumping (NetPad/O2Html)
- **NuGet Support**: Dynamic package resolution
- **EF Core Ready**: Built-in database support
- **Git-Friendly Storage**: .lqpkg zip format with Developer Mode

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
- [ ] Developer Mode (folder structure)
- [ ] Pack/unpack commands

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

### Phase 4: EF Core & Polish
- [ ] EF Core integration
- [x] Connection string injection (via ScriptConfig)
- [ ] Error highlighting
- [ ] ANSI color support
- [ ] Settings UI

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) — Technical design and architecture
- [docs/reference-management.md](docs/reference-management.md) — NuGet and assembly reference pipeline
- [docs/dump-workflow.md](docs/dump-workflow.md) — `.Dump()` HTML output flow
- [docs/intellisense-workflow.md](docs/intellisense-workflow.md) — Code completion pipeline
- [docs/method-signature-help-workflow.md](docs/method-signature-help-workflow.md) — Signature help pipeline

## Acknowledgements

Special thanks to [NetPad](https://github.com/tareqimbasher/NetPad) by Tareq Imbasher for the excellent HTML dumping implementation that ScratchpadSharp leverages.

## License

MIT
