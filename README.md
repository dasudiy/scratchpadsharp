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

## Requirements

- **.NET 8** — SDK to build; [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for framework-dependent publish output.

The output pane uses the platform WebView (`NativeWebView`):

| Platform | WebView engine | Extra install |
|----------|----------------|---------------|
| **Windows** | [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (Evergreen Runtime) | Install the runtime if missing; most Windows 10/11 systems already have it. Published builds embed a Windows app manifest required for native WebView hosting. |
| **Linux** | WebKitGTK | `sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0` (Debian/Ubuntu). If WebKitGTK is missing, the output pane shows an install hint instead of crashing. |
| **macOS** | WKWebView | None (system WebKit). |

## Build & Run

```bash
dotnet build
dotnet run --project src/ScratchpadSharp/ScratchpadSharp.csproj
```

### Windows publish

```bash
dotnet publish src/ScratchpadSharp/ScratchpadSharp.csproj -c Release -r win-x64
```

Ship the **entire** `publish/` folder (`ScratchpadSharp.exe` plus every `.dll` beside it). Do not use `-p:PublishSingleFile=true` or trimming: Roslyn scripting needs real assembly files on disk, and WebView2 needs a normal Win32 host with the embedded `app.manifest`.

The target machine needs:

- [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (framework-dependent publish)
- [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed on Windows 10/11)

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
