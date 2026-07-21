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

Change provider per query in **References (F4) → Script → Database provider**, then **Apply**, or use the **Database (F6)** window. To change the global default, edit `ScriptDefaults` in `appsettings.user.json` — the Settings window does not edit `ScriptDefaults` yet.

**Inheritance:** if the per-query connection string is empty/whitespace, execution injects `ScriptDefaults.ConnectionString` (`ConfigurationLoader.ResolveConnectionString`). Switching provider replaces the connection string when it was empty or still equal to the previous provider's template.

## Database window (F6)

Toolbar **Database** / **F6** opens a host-side explorer (ADO.NET, not the script's EF ALC):

- Choose **SQLite** or **SQL Server**, edit the connection string
- **Test Connection** — reports success/failure, elapsed ms, and server version
- **Refresh Schema** — tree of tables/views and columns (type, nullability, PK)
- **Apply to query** — writes provider + connection string into the tab `ScriptConfig` and resolves matching EF NuGet packages

Host packages: `Microsoft.Data.Sqlite` and `Microsoft.Data.SqlClient` on `ScratchpadSharp.Core`.

## Package loading

New tabs become editable immediately (BCL-only shell). Configured NuGet packages resolve in the background (`Loading packages...`). **Run (F5)** awaits any in-flight resolve before compiling. Re-resolve via **References (F4) → Restore Packages**.

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

- After switching provider, update `Use*` in your `DbContext` to match.
- SQLite `Data Source=...` paths are relative to the process working directory unless absolute.
- Native SQLite assets for scripts still resolve through the NuGet / ALC probing path.

## Roadmap

- **Phase D:** typed entity/`DbContext` scaffold from schema, ad-hoc SQL tab, Settings UI for ScriptDefaults.
