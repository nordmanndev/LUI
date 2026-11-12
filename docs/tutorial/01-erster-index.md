# 1 — Your first index

You start with an empty database file and end with a search that answers. One
index, three entries, one query.

## The catalog

Everything starts with the catalog. It knows the connection string, the
indexes of the database and the lifetime of both. You open it, use it, and
dispose it when the application ends.

```csharp
using Nordmann.LUI;

// The catalog is the entry point: one database, every index in it.
using var catalog = LuiCatalog.Open("Data Source=app.db");
```

That is the whole ceremony. The file does not exist yet — LUI creates it, and
creates its own table `lui_meta` in it, where the definitions of the indexes
are kept.

Every example on this page is a complete program: each one opens the catalog
again, because the catalog is a singleton of the application, not a thing you
pass around.

> **Why so?**
> The catalog checks the SQLite it runs on before it hands out an index: the
> trigram tokenizer of FTS5 needs SQLite 3.45 or newer, and a file that folds
> text differently from the one the library was measured with is refused with
> a message that says which version is needed. Better once, at the door, than
> in the middle of a query. See ADR-0007.
## The first index

An index is described, not configured. `Create` takes the name and a
description — the kind of the index — and does the creating itself.

```csharp
using Nordmann.LUI;

// The catalog is the entry point: one database, every index in it.
using var catalog = LuiCatalog.Open("Data Source=app.db");

// An index of single words: tags, hashtags, names of things.
var tags = catalog.Create("tags", index => index.SingleWord());

tags.Upsert(1, "vegan", weight: 120);
tags.Upsert(2, "vegetarisch", weight: 80);
tags.Upsert(3, "glutenfrei", weight: 40);

foreach (var hit in tags.Search("veg").Hits)
{
    Console.WriteLine($"{hit.Key}: {hit.Text} ({hit.Weight})");
}
```

The output:

```text
1: vegan (120)
2: vegetarisch (80)
```

`SingleWord` searches the input as a whole; `MultiWord` splits it into words.
Which one is right for your data — the short version is in the
[README](../../README.md).

An entry carries the key (the number or the text your application knows the row
by), the text (what is searched and shown) and the weight (the popularity). A
fourth value — the priority — decides where an entry stands when the text
matches equally well; the README has the order.

The key is a `LuiKey`: a number or a text, and it is made from a `long` or a
`string` without a cast. `1` and `"1"` are two different keys.

## The first query

`Search` takes the input and gives the answer. The fluent builder is there for
the cases where the defaults are not what you want.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");

// The index of the step before — the runtime path opens it.
var tags = catalog.Index("tags");

var result = tags
    .Query("veg")
    .Take(5)
    .Rank(LuiRankingOrder.WordStartThenWeight)
    .Execute();

Console.WriteLine($"{result.Hits.Count} of {result.CandidatesConsidered} candidates");
```

```text
2 of 2 candidates
```

Every knob of the builder has a counterpart in `LuiQueryOptions`, which you
can bind from a configuration file and hand to `Search(text, options)`. The
list is in the [README](../../README.md); the meaning of the values is in the
[API](../../docs/api.md).

> **Why so?**
> The builder and the options are the same thing in two shapes. A builder is
> good in code, where the type checker sees the mistake; an options record is
> good in a file, where the values arrive as text. Having one of them and not
> the other would mean two ways to say the same thing — and one of them would
> drift.

## One text, one entry

A query answers with one hit per entry. Two entries that carry the same text
in two spellings — "vegan" and "Vegan" — are two hits: the library does not
hide the second one, because it cannot know which of them your application
means. The uniqueness of a text belongs to the data, and it is stated there:

- **Embedded** — the entries live in the table of the library, and their
  normalised text is the generated column `s_norm` of that table. A **unique
  constraint on the normalised text** is what makes a text unique; the library
  does not create one, because only your application knows whether two entries
  may share a text.
- **Linked** — the table is your own, and so is the constraint: a unique
  constraint over the normalised text of your own column, in your own
  migration. Step 2 shows the table of an application.

> **Why so?**
> The library cannot know which of two spellings the user should see: the
> system tag "vegan" and the ordinary tag "Vegan" are two entries, and only
> the application knows which one belongs in the list. Looking for the
> duplicates in every query is the expensive way to solve it — it keeps the
> folded text of every candidate in memory and it makes the search shallower,
> because a duplicate uses up the candidate budget. In the data it costs one
> constraint. See ADR-0055.

## A typo is forgiven

Typo tolerance is off unless you ask for it. An index that has it corrects an
input that no word of the index begins with — by one edit — and searches what
the correction says.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");

// An index that forgives one typo in an input that no word begins with.
var places = catalog.Create("places", index => index.SingleWord().TypoTolerance());

places.Upsert(1, "Bahnhof", weight: 10);
places.Upsert(2, "Hauptbahnhof", weight: 20);
places.Upsert(3, "Bahnhofstrasse", weight: 30);

// the application asks for the dictionary of the typos — a query never does
places.WarmUpTypoDictionary();

var result = places.Search("bahnhfo");

Console.WriteLine(result.CorrectedQuery);
foreach (var hit in result.Hits)
{
    Console.WriteLine(hit.Text);
}
```

```text
bahnhof
Bahnhof
Bahnhofstrasse
Hauptbahnhof
```

`CorrectedQuery` says what was searched instead of what was typed — it is
`null` when nothing was corrected. A query that needed a correction but could
not get one answers without it and says so in `CorrectionSkipped`; it never
waits for the build and it never starts one — the application asks for the
dictionary with `WarmUpTypoDictionary()` and builds it when it needs it.

Call `WarmUpTypoDictionary()` once the entries are in — at the start of your
application, or after a load. Without it the index answers every query, and
corrects none. The dictionary costs memory, and step 4 has the measured size.

> **Why so?**
> The correction is a *search* over the word beginnings of the index, not a
> spell checker: the dictionary knows the words of your data, and nothing
> else. That is why the correction of "bahnhfo" is "bahnhof" — and why a
> dictionary of the whole German language would correct a word that is not in
> your data at all.

## Creating and opening are two calls

`Create` is the write path: it creates the tables of the index if they are
missing, writes the definition into `lui_meta`, and compares the definition
that is stored with the one you describe. It belongs in the migration of your
application, where a run happens once.

`Index` is the runtime path: it reads the definition out of the file, rebuilds
the index from it, and **writes nothing** — no table, no definition, no write
lock.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");

// The migration of the application creates the index.
var created = catalog.Create("tags", index => index.SingleWord());

// The runtime path opens it — and writes nothing.
var opened = catalog.Index("tags");

Console.WriteLine($"{created.Count()} entries, opened: {opened.Count()}");
```

```text
3 entries, opened: 3
```

Calling `Create` twice is not a mistake: the second call finds the index and
compares the definition with the stored one. A difference is a
`LuiDefinitionMismatchException` that says what differs — the library never
rebuilds an index silently.

> **Why so?**
> A service that may only read — a replica, a file that is read-only, a
> process that is not allowed to write — must not have to know that. If
> opening an index wrote anything, every read-only deployment would need a
> second code path, and the two paths would drift. See ADR-0033.
## A key that is not a number

Most applications identify a row by a number. Some have a slug, a ULID or a
GUID — and then the key is a text. That is a part of the definition, so it is
said once, when the index is created.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");

// A key that is not a number: a slug, a ULID, a GUID as text.
var slugs = catalog.Create("slugs", index => index.SingleWord().WithKey(LuiKeyKind.Text));

slugs.Upsert("01JX8V5M9T", "Apfelkuchen");

var hit = slugs.Search("apfel").Hits[0];
Console.WriteLine($"{(string)hit.Key}: {hit.Text}");
```

```text
01JX8V5M9T: Apfelkuchen
```

A linked index always has the numbers of its table — its `INTEGER PRIMARY
KEY` — and refuses a text key.

## What you have now

- A catalog that owns one database file.
- An index of single words, with three entries.
- A query that returns its hits, best first.

Next: the same idea, but over a table **you** own — step 2,
[Your own data](./02-eigene-daten.md).
