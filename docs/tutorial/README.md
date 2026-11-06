# Tutorial — from an empty database to an autocomplete search

LUI is a library: it puts a fast, typo-tolerant search in front of the data
that is already in your SQLite database. This tutorial is the way there. Five
steps, each with code that runs, from an empty file to a search endpoint.

You will build one small application — a cookbook with tags and articles — and
put two indexes on it: one that LUI owns, and one that reads your own table.
At the end you have a search endpoint, a payload that travels with every hit,
and a stock of a hundred thousand entries you know the cost of.

**Every code block in this tutorial compiles.** The examples are the ones of
the [sample application](../../samples/Lui.Sample/), reduced to what a first
hour needs — and they are built against the library before they are published
here.

## What you need

- The .NET 10 SDK and a SQLite that is 3.45 or newer (the library checks the
  SQLite it finds and says so if it is too old).
- The two packages:

```text
dotnet add package Nordmann.LUI            # the library — all a console application needs
dotnet add package Nordmann.LUI.Hosting    # AddLui and the catalog, for ASP.NET Core or a worker
```

The packages are not on nuget.org yet; the publish pipeline waits for its tag.
Until then, reference the projects of the repository directly.

## The steps

| Step | What it does | Your time at the keyboard |
|---|---|---|
| [1 — Your first index](./01-erster-index.md) | the catalog, the first index, the first entries, the first query | 15 minutes |
| [2 — Your own data](./02-eigene-daten.md) | an index over your own table, the reconciliation, the triggers | 20 minutes |
| [3 — The payload](./03-nutzdaten.md) | the serialiser of the catalog, `GetPayload<T>()` | 15 minutes |
| [4 — Many entries](./04-auf-vielen-eintraegen.md) | `Sync`, the limits, what it costs — with the measured numbers | 20 minutes |
| [5 — In the application](./05-in-der-anwendung.md) | `AddLui` in ASP.NET Core, the catalog from the container, only reading | 15 minutes |

About an hour and a half in all, if you type the code. The reading is the
other half of it.

Every step uses the same database file, `app.db`, and builds on the step
before it. Delete the file and you start over.

## What you can do afterwards

- Search your own rows — from a table you already have, with no second copy of
  the data.
- Write through the library (`Upsert`, `UpsertMany`, `Sync`) or with your own
  SQL and let the index follow (`Refresh`, or the triggers you ask for).
- Give every hit a payload of your own type, read where you use it.
- Rank by popularity, by priority, by how similar the input is, or by the
  beginning of a word — and forgive one typo.
- Put the catalog into an ASP.NET Core container and ask it for an index at
  run time.
- Open a file that may not be written and search it — the file decides what
  may happen, not the index.
- Know what the thing costs before you fill it: every number in step 4 is
  measured, with the machine named.

## The three rules of the road

You will see these again in every step. They are the shape of the API, not
opinions.

1. **One catalog per database.** `LuiCatalog.Open(connectionString)` is the
   entry point. The catalog knows the connection string, the indexes and the
   lifetime; an index is asked for, never registered.
2. **`Create` writes, `Index` does not.** `Create` belongs in the migration of
   your application — it creates the tables of the index and compares the
   stored definition with the one you describe. `Index` (and `LinkedIndex`)
   belongs in the runtime path: it reads the definition out of the file and
   writes nothing at all.
3. **The index knows no types.** A key is a number or a text, a payload is
   bytes, and the type stands where you use it — `hit.GetPayload<TagChip>()`.
   The file does not name a type, so it does not have to be migrated when
   yours changes.

> **Why so?**
> The catalog is the entry point because an index is a table in *your* file:
> whoever knows the connection string has to be the one that owns the indexes
> in it. Splitting it — a catalog that opens the file, an index service that
> holds the indexes — would mean two things that have to agree on the file.
> The decision and its reasons are in
> ADR-0033
> and ADR-0035.

## Where to go next

- **[The README](../../README.md)** — what LUI is, in a page.
- **[The API](../../docs/api.md)** — every public type of the package, with a
  sentence for each.
- **[The benchmarks](../../docs/benchmarks.md)** — every measured number, with
  the machine, the corpus and the method.
- **[The sample](../../samples/Lui.Sample/)** — the whole application behind
  this tutorial, with a page you can click through.
