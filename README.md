# ScratchpadSharp

A lightweight, high-performance C# script runner built with Avalonia UI and Roslyn.

## Features

- **Fast Script Execution**: Roslyn-based C# compilation
- **Memory Isolation**: AssemblyLoadContext with unloading
- **IntelliSense Support**: Code completion, signature help, and formatting
- **Multi-Tab Editing**: Independent Roslyn project per tab
- **Rich Object Visualization**: HTML-based dumping (NetPad/O2Html)
- **NuGet Support**: Dynamic package resolution
- **Module System**: EF Core database modules with sidebar, query refs, and merged compile
- **Git-Friendly Storage**: .lqpkg zip format with Developer Mode folder layout
- **Session Restore**: Reopen tabs, unsaved code, and references after restart (configurable)

## Project Structure

```
src/
├── ScratchpadSharp/          # Avalonia UI application
├── ScratchpadSharp.Core/     # Script execution, modules, storage
└── ScratchpadSharp.Shared/   # Shared models and exceptions
```

## Build & Run

```bash
dotnet build
dotnet run --project src/ScratchpadSharp/ScratchpadSharp.csproj
```

The output pane uses the platform WebView. On Linux install WebKitGTK:

```bash
sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0
```

### GNOME Desktop Icon

Window/taskbar icons load from embedded assets. To appear in the **GNOME app grid**, install the freedesktop entry once:

```bash
dotnet build -c Release
chmod +x scripts/install-desktop-entry.sh
./scripts/install-desktop-entry.sh
```

### Headless script run (debug / CI)

```bash
dotnet run --project src/ScratchpadSharp/ScratchpadSharp.csproj -- --headless run \
  --module <moduleInstanceId> \
  --code 'await using var db = new Modules.MyDb.AppDbContext(); db.Orders.Take(1).Dump();'
```

Use `--file path/to/script.cs` instead of `--code`. Module id is the folder name under `{LocalApplicationData}/ScratchpadSharp/modules/`.

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) — Technical design and architecture
- [docs/ef-core.md](docs/ef-core.md) — EF Core modules and database sidebar
- [docs/ssh-tunnel.md](docs/ssh-tunnel.md) — Optional SSH tunnel for SQL Server (and later TCP databases)
- [docs/reference-management.md](docs/reference-management.md) — NuGet and assembly reference pipeline
- [docs/session-restore.md](docs/session-restore.md) — Session persistence
- [docs/dump-workflow.md](docs/dump-workflow.md) — `.Dump()` HTML output flow
- [docs/intellisense-workflow.md](docs/intellisense-workflow.md) — Code completion pipeline

## Acknowledgements

Special thanks to [NetPad](https://github.com/tareqimbasher/NetPad) by Tareq Imbasher for the excellent HTML dumping implementation that ScratchpadSharp leverages.

## License

This project is licensed under the [MIT License](LICENSE).
