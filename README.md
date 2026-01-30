# ScratchpadSharp

A lightweight, high-performance C# script runner built with Avalonia UI and Roslyn.

## Features

- **Fast Script Execution**: Roslyn-based C# compilation
- **Memory Isolation**: AssemblyLoadContext with unloading
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

### Phase 1: MVP (In Progress)
- [x] Project structure and dependencies
- [x] Basic Avalonia UI with AvaloniaEdit
- [x] Simple script execution (no isolation)
- [x] Console output redirection
- [x] Save/load .lqpkg files

### Phase 2: Isolation & Storage
- [ ] AssemblyLoadContext implementation
- [ ] ALC unloading with WeakReference
- [ ] Developer Mode (folder structure)
- [ ] Pack/unpack commands

### Phase 3: NuGet & Dumpify
- [ ] NuGet package resolution
- [ ] Dumpify integration
- [ ] MetadataReference management
- [ ] config.json support

### Phase 4: EF Core & Polish
- [ ] EF Core integration
- [ ] Connection string injection
- [ ] Error highlighting
- [ ] ANSI color support
- [ ] Settings UI

## Documentation

See [SPECIFICATION.md](SPECIFICATION.md) for detailed technical design.

## License

MIT (TBD)
