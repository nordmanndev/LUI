# 3 — The payload

A hit is more than a key and a text. Often the application wants to show
something next to it: a colour, a category, a second id of its own.

That something is the **payload**. LUI stores it with the entry and gives it
back with the hit.

## The serialiser of the catalog

The payload is written by **one** serialiser — the one of the catalog. Not one
per index, and not one per payload type: the catalog has one, and it is set
when the catalog is opened.

The usual one is JSON, with the source-generated type descriptions of your
application. That makes it safe for trimming and AOT, and it makes a payload
type something your application lists in one place.

```csharp
using System.Text.Json.Serialization;
using Nordmann.LUI;

// The catalog is opened with the serialiser — it belongs to the catalog.
using var catalog = LuiCatalog.Open("Data Source=app.db", new LuiCatalogOptions
{
    PayloadSerializer = LuiPayloadSerializer.Json(TagJson.Default),
});

// The index is the one of chapter 1 — the payload does not change the definition.
var tags = catalog.Create("tags", index => index.SingleWord());

tags.Upsert(new LuiEntry(1, "vegan") { Weight = 120, Payload = new TagChip("#3a3", "System") });

// The type stands where the payload is used.
var chip = tags.Search("veg").Hits[0].GetPayload<TagChip>();

Console.WriteLine($"{chip!.Colour} {chip.Group}");

// The payload type of the application.
public sealed record TagChip(string Colour, string Group);

[JsonSerializable(typeof(TagChip))]
public sealed partial class TagJson : JsonSerializerContext;
```

```text
#3a3 System
```

Two things are worth reading twice.

**The serialiser belongs to the catalog.** A catalog without one stores no
payloads, and an entry that carries one is refused. A catalog with one writes
the payload of *every* embedded index with it — which is why the JSON context
lists every payload type of the application, not one per index.

**The type stands where the payload is used.** The file stores bytes, not a
type name. `GetPayload<TagChip>()` says what you expect to get, and the
serialiser of the catalog reads the bytes as that type. Read it as another
type and you get a `LuiPayloadException` that names the index, the key and the
type — never an object full of default values that looks like data.

> **Why so?**
> The file names no type. A type in the file is a promise that outlives the
> code that made it: change the record, and every database in the field holds
> a payload that no longer fits. Bytes do not have that problem — the
> application decides what they mean when it reads them. See ADR-0035.
The payload of an entry is written with the entry. `Upsert` replaces the whole
entry — text, weight, priority **and payload** — so an entry without a payload
clears the payload that was stored. To change only the weight or the
priority, use `UpdateWeight` and `UpdatePriority`.

## The typed view — the payload in your type

`GetPayload<TagChip>()` writes the type at every place that reads the
payload — and a typo in it is an error at run time. If you want the compiler
to check the type, ask for a **view** of the index:

```csharp
var chip = tags.As<TagChip>().Search("veg").Hits[0].GetPayload();

// the fluent query ends in the same typed answer
var first = tags.Query("veg").Take(5).Execute().As<TagChip>().Hits[0];
```

The view is a view: it opens nothing, writes nothing and holds no index of its
own. It mirrors the three untyped types — `LuiTypedIndex<TagChip>`,
`LuiTypedResult<TagChip>` and `LuiTypedHit<TagChip>` — and the key stays a
`LuiKey`. `GetPayload()` reads the payload on every call, exactly like
`GetPayload<T>()` — a method, because it does work and can fail; keep the
result if you need it twice.

The untyped way stays: `GetPayload<TagChip>()` is still there, and it stays
the one way for an application that wants no view.

## The payload of a linked index

A linked index stores no payload: the row is yours, and the payload is built
from its columns when the hit is read. Name the columns and give a map.

```csharp
using Microsoft.Data.Sqlite;
using Nordmann.LUI;

using (var connection = new SqliteConnection("Data Source=app.db"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS tag (
            id     INTEGER PRIMARY KEY,
            name   TEXT    NOT NULL,
            kind   TEXT    NOT NULL,
            colour TEXT    NOT NULL
        );
        INSERT OR REPLACE INTO tag VALUES (1, 'vegan', 'Ernährung', '#3a3');
        """;
    command.ExecuteNonQuery();
}

using var catalog = LuiCatalog.Open("Data Source=app.db");

// A linked index with a payload: the columns of the payload are named, the map builds it.
var tags = catalog.Create(
    "app_tags",
    index => index.SingleWord()
        .LinkTo(table: "tag", key: "id", text: "name")
        .WithPayload(["kind", "colour"], row => new TagPayload(row.GetString("kind"), row.GetString("colour"))));

tags.Sync();

var payload = tags.Search("vegan").Hits[0].GetPayload<TagPayload>();
Console.WriteLine($"{payload!.Kind} {payload.Colour}");

// The payload type of the application.
public sealed record TagPayload(string Kind, string Colour);
```

```text
Ernährung #3a3
```

The row of a `LuiPayloadRow` is valid only while the map runs — copy the
values you need out of it. The columns of the payload are part of the
definition of the index, so an index with payload columns is opened with its
map again:

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");

// The map is code of the application — it is handed over when the index is opened.
var tags = catalog.LinkedIndex<TagPayload>(
    "app_tags",
    row => new TagPayload(row.GetString("kind"), row.GetString("colour")));

var payload = tags.Search("vegan").Hits[0].GetPayload<TagPayload>();
Console.WriteLine($"{payload!.Kind} {payload.Colour}");

// The payload type of the application.
public sealed record TagPayload(string Kind, string Colour);
```

```text
Ernährung #3a3
```

## The types of the application

The payload types are yours, and they live wherever your application keeps
its types. The JSON context is the list of them:

```csharp
using System.Text.Json.Serialization;

public sealed record TagChip(string Colour, string Group);

public sealed record TagPayload(string Kind, string Colour);

[JsonSerializable(typeof(TagChip))]
public sealed partial class TagJson : JsonSerializerContext;
```

A payload type that is not in the context is refused when it is written or
read, and the message names it — there is no reflection and no guessing.

## What you have now

- A payload that travels with every hit, of a type of your own.
- One serialiser for the whole catalog.
- A linked index that builds its payload from your own columns.

Next: what happens when there are a hundred thousand entries — step 4,
[Many entries](./04-many-entries.md).
