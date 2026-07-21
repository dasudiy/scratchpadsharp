# EF Core in ScratchpadSharp

ScratchpadSharp ships EF Core + a selectable database provider in `ScriptDefaults` so new scripts can query a database without manually adding packages.

## Defaults

From `appsettings.json` → `ScriptDefaults` (overridable via `appsettings.user.json` or per-query `config.json`):

| Setting | Default |
|---------|---------|
| `DatabaseProvider` | `Sqlite` (`None` / `Sqlite` / `SqlServer`) |
| NuGet | `Microsoft.EntityFrameworkCore` 8.0.11 + provider package (see below) |
| Using | `Microsoft.EntityFrameworkCore` |
| Connection string | `Data Source=scratchpad.db` |

### Provider → NuGet mapping

| Provider | Packages | `OnConfiguring` helper |
|----------|----------|------------------------|
| `None` | (removes EF packages) | — |
| `Sqlite` | `Microsoft.EntityFrameworkCore` + `.Sqlite` | `UseSqlite` |
| `SqlServer` | `Microsoft.EntityFrameworkCore` + `.SqlServer` | `UseSqlServer` |

Change provider per query in **References (F4) → Script → Database provider**, then **Apply** (swaps NuGet packages and resolves). To change the global default, edit `ScriptDefaults.DatabaseProvider` / packages in `appsettings.user.json` — the Settings window does not edit `ScriptDefaults` yet.

**Inheritance:** if the per-query connection string is empty/whitespace, execution injects `ScriptDefaults.ConnectionString` (`ConfigurationLoader.ResolveConnectionString`). Switching provider replaces the connection string when it was empty or still equal to the previous provider's template. **Reset** in F4 → Script restores ScriptDefaults values in the UI; click **Apply** to write packages/config.

## Package loading

New tabs become editable immediately (BCL-only shell). Configured NuGet packages (including EF Core) resolve in the background; the status bar shows `Loading packages...`. **Run (F5)** awaits any in-flight resolve before compiling (`Waiting for packages...` if still loading), so EF scripts do not fail with missing-reference errors from racing the download. Re-resolve manually via **References (F4) → Restore Packages** when NuGet packages are configured.

## Script API

Inside a script, `ConnectionString` is a local string injected from the effective connection string (query config, or ScriptDefaults when empty).

```csharp
public class Blog
{
    public int BlogId { get; set; }
    public string Url { get; set; } = "";
}

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite(ConnectionString); // UseSqlServer(...) after switching provider
}

await using var db = new BloggingContext();
await db.Database.EnsureCreatedAsync();

db.Blogs.Add(new Blog { Url = "https://example.com" });
await db.SaveChangesAsync();

db.Blogs.ToList().Dump("Blogs");
```

## Notes

- After switching provider, update `Use*` in your `DbContext` to match (F4 hint shows the method name).
- The SQLite file path in `Data Source=...` is relative to the process working directory unless absolute.
- Native SQLite assets are resolved through the existing NuGet / ALC native probing path.

## Roadmap

- **Phase C:** dedicated Database window (F6): test connection, schema tree (ADO.NET on host).
- **Phase D:** typed scaffold, ad-hoc SQL, Settings UI for ScriptDefaults.
