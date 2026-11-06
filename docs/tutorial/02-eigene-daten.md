# 2 — Your own data

Step 1 put the entries into a table of LUI's own. Most applications already
have their data in a table they wrote themselves — and they do not want a
second copy of it.

This step builds an index over **your** table: LUI reads it, indexes it, and
never changes it.

## The table of the application

Your table is your table. LUI does not create it, does not add a column to it,
does not put an index on it and does not put a trigger on it — unless you ask
for one.

```csharp
using Microsoft.Data.Sqlite;

// The application's own table. LUI reads it and never changes it.
using (var connection = new SqliteConnection("Data Source=app.db"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS article (
            id    INTEGER PRIMARY KEY,
            title TEXT    NOT NULL,
            views INTEGER NOT NULL DEFAULT 0
        );
        INSERT OR REPLACE INTO article VALUES
            (1, 'Gedeckter Apfelkuchen', 9000),
            (2, 'Apfelstrudel', 5000),
            (3, 'Bratapfelkuchen', 800);
        """;
    command.ExecuteNonQuery();
}
```

Two things make this table fit a linked index: the key is an
`INTEGER PRIMARY KEY`, and the text is a column of text.

## The linked index

`LinkTo` says which columns are which. The result is an index that searches
the table — no copy of the row, no second source of truth.

```csharp
using Microsoft.Data.Sqlite;
using Nordmann.LUI;

// The application's own table. LUI reads it and never changes it.
using (var connection = new SqliteConnection("Data Source=app.db"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS article (
            id    INTEGER PRIMARY KEY,
            title TEXT    NOT NULL,
            views INTEGER NOT NULL DEFAULT 0
        );
        INSERT OR REPLACE INTO article VALUES
            (1, 'Gedeckter Apfelkuchen', 9000),
            (2, 'Apfelstrudel', 5000),
            (3, 'Bratapfelkuchen', 800);
        """;
    command.ExecuteNonQuery();
}

using var catalog = LuiCatalog.Open("Data Source=app.db");

// A linked index: the text is the column "title", the key is the column "id",
// the popularity is the column "views".
var articles = catalog.Create(
    "articles",
    index => index.MultiWord().LinkTo(table: "article", key: "id", text: "title", weight: "views"));

// The index starts empty — the sync fills it.
var report = articles.Sync();
Console.WriteLine($"{report.Indexed} rows indexed");

foreach (var hit in articles.Search("apfelkuchen").Hits)
{
    Console.WriteLine($"{hit.Key}: {hit.Text} ({hit.Weight})");
}
```

```text
3 rows indexed
1: Gedeckter Apfelkuchen (9000)
3: Bratapfelkuchen (800)
```

The index starts empty — that holds after a `DropIndex` too. `Sync()` walks
the table and fills the index; a row without a text is left out and counted,
never refused.

> **Why so?**
> A linked index keeps no copy of the row: the search index holds the
> normalised text, LUI's own table holds the weight and the priority, and the
> text of a hit is read from your table when the hit is read. A second copy of
> your data is the most expensive thing an autocomplete can do — it doubles
> the storage and it is always out of date. See
> [ADR-0015](../../docs/adr/0015-the-linked-index-is-loosely-coupled.md).

## A write of the application: `Refresh`

Your application changes its table. The index has to hear about it — either
every time, or in batches.

`Refresh` brings one row in step: after your own write, in your own
transaction if you like.

```csharp
using Microsoft.Data.Sqlite;
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
var articles = catalog.Create(
    "articles",
    index => index.MultiWord().LinkTo(table: "article", key: "id", text: "title", weight: "views"));

using (var connection = new SqliteConnection("Data Source=app.db"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "UPDATE article SET title = 'Apfelkuchen mit Vanillesauce' WHERE id = 1";
    command.ExecuteNonQuery();
}

// The index follows the row.
articles.Refresh(1);

Console.WriteLine(articles.Search("vanillesauce").Hits[0].Text);
```

```text
Apfelkuchen mit Vanillesauce
```

`RefreshMany` does the same for many rows in one transaction. A row that is
gone, or has no text, leaves the index.

## The triggers

Telling LUI after every write is work, and it is work you can forget. The
triggers are the other way round: your table notes the key of every changed
row, and LUI brings the noted rows into the index when you tell it to.

The triggers are opt-in. Ask for the script and put it in your own migration,
or let LUI create them:

```csharp
using Microsoft.Data.Sqlite;
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
var articles = catalog.Create(
    "articles",
    index => index.MultiWord().LinkTo(table: "article", key: "id", text: "title", weight: "views"));

// The script for the application's own migration.
Console.WriteLine(articles.TriggerScript());

// Or: create the triggers right away.
articles.CreateTriggers();
Console.WriteLine(articles.Triggers());

// The application writes its own table — the trigger only notes the key.
using (var connection = new SqliteConnection("Data Source=app.db"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "INSERT INTO article (title, views) VALUES ('Apfelstrudel mit Vanillesauce', 50)";
    command.ExecuteNonQuery();
}

Console.WriteLine($"{articles.Pending()} row(s) noted");

// The index follows, in batches of a thousand.
articles.ApplyPending();

Console.WriteLine($"{articles.Pending()} row(s) noted");
Console.WriteLine(articles.Search("vanillesauce").Hits[0].Text);
```

```text
CREATE TRIGGER lui_articles_ai AFTER INSERT ON "article" BEGIN INSERT OR IGNORE INTO lui_articles_pending(doc_id) VALUES (new."id"); END;
CREATE TRIGGER lui_articles_au AFTER UPDATE OF "id", "title", "views" ON "article" BEGIN INSERT OR IGNORE INTO lui_articles_pending(doc_id) VALUES (old."id"); INSERT OR IGNORE INTO lui_articles_pending(doc_id) VALUES (new."id"); END;
CREATE TRIGGER lui_articles_ad AFTER DELETE ON "article" BEGIN INSERT OR IGNORE INTO lui_articles_pending(doc_id) VALUES (old."id"); END;
Current
1 row(s) noted
0 row(s) noted
Apfelkuchen mit Vanillesauce
```

`Pending()` is the number of rows the triggers noted that `ApplyPending()` has
not brought in yet — a good value for a health check, and the number that
grows while you do not call it.

> **Why so?**
> The triggers are plain SQL: an `INSERT OR IGNORE` of a key, no FTS5, no
> normalisation, no check. Every program that writes your table can still
> write it, with any SQLite, and a write of your application never fails
> because of the index. The price is that the index is as current as the last
> `ApplyPending()` — so call it regularly, on a timer, after a burst of
> writes.

## What you have now

- A table of your own, untouched by LUI.
- A linked index over it, filled with `Sync`, kept in step with `Refresh` or
  with the triggers.
- Two indexes in one database: one that LUI owns, one that reads your table.

Next: what comes back with a hit — step 3,
[The payload](./03-nutzdaten.md).
