# EF Core Modules

ScratchpadSharp uses **EF Core modules** — reusable database connections with generated `DbContext` / entity models. Queries reference modules instead of embedding connection strings or scaffolded code.

## Create a database module

1. Open the **Modules** sidebar (left pane).
2. Click **+** or use **Add database** in the context menu.
3. Enter display name, provider, and connection (form or raw connection string).
4. **Test connection**, then **Save** — schema is scaffolded into `model.cs` under the module instance.

Modules are stored at:

```
{LocalApplicationData}/ScratchpadSharp/modules/{instanceId}/
  module.json
  model.cs
```

## Reference a module from a query

1. Select a query tab.
2. In the Modules sidebar, right-click an EF Core instance → **Add ref to query**.

The query `config.json` stores `moduleRefs` (instance ids). At compile/run, ScratchpadSharp merges module NuGet packages, usings, and `model.cs` into the query.

## Script API

Each module uses namespace `Modules.{NamespaceSegment}` from the instance display name:

```csharp
await using var db = new Modules.LocalSqlite.AppDbContext();
db.Blogs.Take(100).Dump();
```

Connection strings are baked into the generated `OnConfiguring` — there is no ambient `ConnectionString` in scripts.

Scaffolded models map each entity with `.ToTable("ExactTableName", "schema")` so pluralized `DbSet` names (e.g. `SalesTicketOrders`) still query the real SQL table (`SalesTicketOrder`). After upgrading ScratchpadSharp, use **Regenerate model** on existing modules so `model.cs` picks up this mapping.

## Sidebar actions

| Action | Description |
|--------|-------------|
| Refresh | Reload schema tree |
| Edit connection | Change provider / connection string |
| Regenerate model | Re-scaffold `model.cs` from live schema |
| Add / Remove ref | Toggle module on active query |
| Take (100) / Count | Run ephemeral LINQ against the module |
| Delete | Remove module instance |

## Providers

SQLite and SQL Server are supported (same as before). Provider packages are pinned on the module instance, not on the query.

## References window (F4)

The **Script** tab shows referenced modules (read-only) and per-query timeout. Add or remove module refs from the Modules sidebar.
