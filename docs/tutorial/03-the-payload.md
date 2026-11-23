# 3 — The payload

A hit is more than a key and a text. Often the application wants to show
something next to it: a colour, a category, a second id of its own.

That something is the **payload**. LUI stores it with the entry and gives it
back with the hit.

## The registry of the catalog

The payload is written by a serializer **of its type**: the catalog holds a registry, one serializer per payload type, and it is set
when the catalog is opened. A definition with a payload type — `LuiIndexDefinition<long, TagChip>` — names the type once.

The usual serializer is JSON, with the source-generated type descriptions of your
application. That makes it safe for trimming and AOT, and it makes a payload
type something your application lists in one place.

```csharp
using System.Text.Json.Serialization;
using Nordmann.LUI;

// The catalog is opened with the registry of the serializers of its payload types.
using var catalog = LuiCatalog.OpenOrCreate("app.db", new LuiCatalogOptions
{
    Payloads = new LuiPayloadRegistry().Use(TagJson.Default),
});

// The index declares its payload type once, in its definition.
var tags = catalog.Create(new LuiIndexDefinition<long, TagChip>("tags", index => index.SingleWord()));

tags.Upsert(new LuiEntry<long, TagChip>(1, "vegan", new TagChip("#3a3", "System")) { Weight = 120 });

// The payload comes with the hit, in its type.
var chip = tags.Search("veg").Hits[0].Payload;

Console.WriteLine($"{chip.Colour} {chip.Group}");

// The payload type of the application.
public sealed record TagChip(string Colour, string Group);

[JsonSerializable(typeof(TagChip))]
public sealed partial class TagJson : JsonSerializerContext;
```

```text
#3a3 System
```

Two things are worth reading twice.

**The serializer belongs to the payload type.** A catalog without a registry refuses an index with a payload type, when
the index is created, and a type that the registry does not know is refused the same way — nothing is written.

**The type is declared once.** The file stores bytes and the *name* of the payload type and the format of its serializer, and the
handle of the definition reads the bytes as that type. Another type or another format is a `LuiDefinitionMismatchException` when the index
is opened; a payload that cannot be read fails the query with a `LuiPayloadException` that names the index, the key and the type — never an
object full of default values that looks like data.

The payload of an entry is written with the entry. `Upsert` replaces the whole
entry — text, weight, priority **and payload**. To change only the weight or the
priority, use `UpdateWeight` and `UpdatePriority`.

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

using var catalog = LuiCatalog.OpenOrCreate("app.db");

// A linked index with a payload: the columns of the payload are named, the map builds it.
var tags = catalog.Create(new LuiLinkedIndexDefinition<long, TagPayload>(
    "app_tags",
    index => index.SingleWord()
        .LinkTo(table: "tag", key: "id", text: "name")
        .WithPayload(["kind", "colour"], row => new TagPayload(row.GetString("kind"), row.GetString("colour")))));

tags.Sync();

var payload = tags.Search("vegan").Hits[0].Payload;
Console.WriteLine($"{payload.Kind} {payload.Colour}");

// The payload type of the application.
public sealed record TagPayload(string Kind, string Colour);
```

```text
Ernährung #3a3
```

The row of a `LuiPayloadRow` is valid only while the map runs — copy the
values you need out of it. The columns of the payload are part of the
definition of the index, so an index with payload columns is opened with the
same definition — the map is part of it:

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.OpenOrCreate("app.db");

// The map is code of the application — it is part of the definition that opens the index.
var tags = catalog.Index(new LuiLinkedIndexDefinition<long, TagPayload>(
    "app_tags",
    index => index.SingleWord()
        .LinkTo(table: "tag", key: "id", text: "name")
        .WithPayload(["kind", "colour"], row => new TagPayload(row.GetString("kind"), row.GetString("colour")))));

var payload = tags.Search("vegan").Hits[0].Payload;
Console.WriteLine($"{payload.Kind} {payload.Colour}");

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

A payload type that is not in the context is refused when the index is created or
opened, and the message names it — there is no reflection and no guessing.

## What you have now

- A payload that travels with every hit, of a type of your own.
- One serializer per payload type, in the registry of the catalog.
- A linked index that builds its payload from your own columns.

Next: what happens when there are a hundred thousand entries — step 4,
[Many entries](./04-many-entries.md).
