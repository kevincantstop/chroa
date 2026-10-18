---
name: persistance
description: This skill should be used when storing or loading local data in Chroa Video Player — adding a settings or history table, using LocalStore<T> or LocalDatabase from ChroaVideoPlayer.Core.Persistance, choosing a table key, capping rows, ordering queries, or debugging why persisted rows are missing, duplicated or unsupported. Covers the model requirements the schema enforces, the upsert and LRU behaviour, and the traps that make data silently disappear.
---

# Local persistence (SQLite) in Chroa Video Player

Everything durable lives in `ChroaVideoPlayer.Core/Persistance/`, a thin key/value layer over
`Microsoft.Data.Sqlite`: the public API at the top, the SQL machinery in `Persistance/Database/`, the
column marker in `Persistance/Attributes/`. The player uses it for per-video settings and its
recent-video list; plugins use the same layer (the playlist plugin keeps its history with it).

## The shape of it

| Type | Role |
| --- | --- |
| `LocalStore<T>` | The public API: one table, keyed by a string |
| `LocalDatabase.DefaultPath` | `%LOCALAPPDATA%\ChroaVideoPlayer\chroa.db` |
| `TableSchema.For<T>()` | Builds the table definition from `T` — **only `[PersistentColumn]` properties become columns**, and **the table name is the class name** |
| `TableRepository<T>` | SQL: upsert, select, delete, count |
| `SqliteTypeMap` | Maps CLR types to SQLite and back |
| `DeferredSaveQueue` | Debounced writes for high-frequency updates (`QueueSave` → 1 s) |
| `LruLimiter<T>` | Trims the oldest rows past a limit |

## Model requirements (enforced, and they throw)

- `T` must be a **class with a public parameterless constructor** (`where T : class, new()`).
- Columns are the **public properties marked `[PersistentColumn]`**
  (`ChroaVideoPlayer.Core.Persistance.Attributes`), and nothing else. An unmarked property is simply not
  a column, so computed display properties (`Title => Path.GetFileName(Path)`) need no special care.
- A model with **no** marked property throws `NotSupportedException` instead of quietly building a table
  with no columns.
- **No property may be named `key`** — the key lives in its own `key` column and a clash throws
  `NotSupportedException`.
- Supported types (via `SqliteTypeMap`): `string`, `bool`, enums, all integer types, `float`, `double`,
  `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`. Anything else throws while the schema is
  built.
- A missing property is simply a missing column; `DateTime` is stored as ISO-8601 round-trip text, which
  means **`ORDER BY` on it sorts chronologically**, and `TimeSpan` is stored as ticks.

## API

```csharp
var store = new LocalStore<MyRow>("settings.db");                       // or (limit, recencyColumn, path)
store.Save(key, row);                                                   // INSERT … ON CONFLICT(key) DO UPDATE
store.Get(key);  store.Contains(key);  store.Delete(key);  store.Count();
store.Query(search: null, orderBy: nameof(MyRow.SavedUtc), descending: true, offset: 0, limit: 50);
store.QueueSave(key, row);                                              // debounced, for frequent edits
store.Flush();
```

- `Save` is an **upsert**: saving the same key again updates that row instead of adding a duplicate.
- `orderBy` is validated against the table's columns and throws on an unknown name; `limit` must be ≥ 1
  and defaults to **50** — pass a bigger limit explicitly if you expect more rows.
- The constructor overload with `(int limit, string recencyColumn, …)` installs an **LRU cap**: it trims
  on construction and after every `Save`, deleting the rows with the oldest recency value.

## Rules learned the hard way

- **Never construct a `LocalStore` eagerly in a plugin's constructor.** The LRU trim runs a query during
  construction, which opens the database and creates the table — and the player instantiates *every*
  discovered plugin at startup, including disabled ones. Build the store lazily on first use.
- **Persistence must never break playback.** Every operation is already wrapped: failures are written to
  `Debug` output and return a fallback (empty list, `false`, `0`). Keep that property — do not add
  throwing paths on top.
- **The table name is the model class name.** Renaming the class silently starts a new table, and the old
  rows look lost. Renaming the model is a data migration, not a rename.
- **Give each concern its own database file** unless it genuinely shares a model. The playlist plugin
  writes `playlist.db` next to the player's `chroa.db` rather than adding tables to the player's schema.
- **One writer per file.** Each call opens and closes its own connection (`Mode=ReadWriteCreate`) and the
  directory and table are created on demand, but two processes hammering the same file will still hit
  SQLite locking.
- **Debounce frequent edits** with `QueueSave` (1 s) — a slider drag should not issue a write per pixel.
  One-off events (a video started) can use `Save` directly.
- **Delete rows, not the database file.** The file stays locked for as long as the process lives (SQLite
  pools its connections), so a "clear this history" feature must call `LocalStore.Delete` per row instead
  of deleting or truncating the file.
- **Keep UI-only state out of the schema by leaving it unmarked.** A `public bool IsPlaying { get; set; }`
  added to drive a "now playing" highlight becomes a real column the moment it is marked
  `[PersistentColumn]` — and the flag then comes back from disk as `true` on the next launch. An unmarked
  property is never a column, and a get-only one cannot be marked in the first place.
- Store UTC (`DateTime.UtcNow`) in any timestamp you intend to order or cap on.
- **Adding a column is a safe, automatic migration.** `TableCreator.EnsureCreated` runs
  `ALTER TABLE … ADD COLUMN` for every schema column the table is missing, and `Materialize` skips
  `DBNull`, so rows written by the older schema come back with the property's default value. That is how
  a new field reaches a database that already holds data — no migration script, no data loss.
- **A manually ordered table needs its own order column.** Ordering by an add timestamp cannot express a
  user reorder, and rewriting that timestamp would lie about when the row arrived. Add an integer column,
  order by it, and on load renumber the legacy rows that come back as `0` *in the previous order* before
  returning them, so upgrading does not reshuffle the list. Persist only the rows whose index moved.

## Worked example

`ChroaVideoPlayer.Plugins.Playlist/Persistance/PlaylistStore.cs` is the smallest complete example: a
dedicated database file, a 200-row LRU cap keyed on `AddedUtc`, a `Load()` that orders by the persistent
`SortIndex` and renumbers legacy rows from their old add order, a `SaveOrder` that writes only the rows
whose position changed, and an upsert-on-save so replaying a file updates its row instead of adding
another one.
