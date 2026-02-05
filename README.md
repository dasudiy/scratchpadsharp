# ScratchpadSharp

A lightweight, high-performance C# script runner built with Avalonia UI and Roslyn.

## Features

- **Fast Script Execution**: Roslyn-based C# compilation
- **Memory Isolation**: AssemblyLoadContext with unloading
- **IntelliSense Support**: Code completion, signature help, and formatting
- **Rich Object Visualization**: Dumpify integration
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

### Phase 3: NuGet & Dumpify (In Progress)
- [x] MetadataReference management with XML docs
- [ ] NuGet package resolution
- [ ] Dumpify integration
- [ ] config.json support

### Phase 4: EF Core & Polish
- [ ] EF Core integration
- [x] Connection string injection (via ScriptConfig)
- [ ] Error highlighting
- [ ] ANSI color support
- [ ] Settings UI

## Documentation

See [SPECIFICATION.md](SPECIFICATION.md) for detailed technical design.

## License

MIT (TBD)
