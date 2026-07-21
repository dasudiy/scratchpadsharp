# EF Core in ScratchpadSharp

ScratchpadSharp ships EF Core + a selectable database provider in `ScriptDefaults` so new scripts can query a database without manually adding packages.

## Defaults

From `appsettings.json` → `ScriptDefaults` (overridable via **Settings → Script defaults** / `appsettings.user.json` or per-query `config.json`):

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

Change provider per query in **References (F4) → Script**, **Database (F6)**, or set globals under **Settings → Script defaults**.

**Inheritance:** empty per-query connection string inherits `ScriptDefaults.ConnectionString` at run time.

## Database window (F6)

Toolbar **Database** / **F6** — host ADO.NET explorer (`Microsoft.Data.Sqlite` / `Microsoft.Data.SqlClient`):

| Action | Behavior |
|--------|----------|
| Test Connection | Success/failure, elapsed ms, server version |
| Refresh Schema | Tables/views → columns (type, nullability, PK) |
| Scaffold selected / all | Inserts EF entity classes + `AppDbContext` + sample query into the active script |
| SQL tab | Ad-hoc SQL on the host; TSV result grid as text |
| Apply to query | Writes provider + connection string into tab `ScriptConfig` and resolves EF NuGet packages |

## Package loading

New tabs are editable immediately; NuGet resolves in the background. **Run (F5)** awaits in-flight resolve. **References (F4) → Restore Packages** re-resolves manually.

## Script API

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

- After switching provider, update `Use*` in your `DbContext` (scaffold does this automatically).
- SQLite `Data Source=...` paths are relative to the process working directory unless absolute.
- Script EF native assets still resolve through NuGet / ALC probing; schema/SQL tools use host drivers.
