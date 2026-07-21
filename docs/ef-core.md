# EF Core in ScratchpadSharp

ScratchpadSharp ships EF Core + the SQLite provider in `ScriptDefaults` so new scripts can query a database without manually adding packages.

## Defaults

From `appsettings.json` → `ScriptDefaults` (overridable via `appsettings.user.json` or per-query `config.json`):

| Setting | Default |
|---------|---------|
| NuGet | `Microsoft.EntityFrameworkCore` 8.0.11, `Microsoft.EntityFrameworkCore.Sqlite` 8.0.11 |
| Using | `Microsoft.EntityFrameworkCore` |
| Connection string | `Data Source=scratchpad.db` |

Edit the connection string in **References (F4) → Script**, or globally under **Settings** / user overrides.

## Script API

Inside a script, `ConnectionString` is a local string injected from the query config (same value as `__ConnectionString`).

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
        => options.UseSqlite(ConnectionString);
}

await using var db = new BloggingContext();
await db.Database.EnsureCreatedAsync();

db.Blogs.Add(new Blog { Url = "https://example.com" });
await db.SaveChangesAsync();

db.Blogs.ToList().Dump("Blogs");
```

## Notes

- Providers other than SQLite: add the NuGet package in Reference Manager (F4) and call the matching `Use*` extension.
- The SQLite file path in `Data Source=...` is relative to the process working directory unless absolute.
- Native SQLite assets are resolved through the existing NuGet / ALC native probing path.
