# 5 — In the application

Everything so far ran in a console application that opened the catalog itself.
In a real application the catalog belongs to the container, and the container
holds the catalog and **nothing else**.

## The package

`AddLui` comes from `Nordmann.LUI.Hosting` — a second package that brings the
dependency injection and nothing else. No background service, no timer, no
opinion about your application.

```text
dotnet add package Nordmann.LUI.Hosting
```

## `AddLui`

One call registers the catalog. It is opened when it is first needed, and it
goes with the host.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI;

var builder = WebApplication.CreateBuilder(args);

// The container holds the catalog and nothing else.
builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"));

var app = builder.Build();

app.MapGet("/suggest", (LuiCatalog catalog, string q) =>
    Results.Ok(catalog.Index("tags").Search(q).Hits.Select(hit => hit.Text)));

// The catalog is injected — the index is asked for.
var catalog = app.Services.GetRequiredService<LuiCatalog>();
Console.WriteLine(catalog.Index("tags").Search("veg").Hits[0].Text);

app.Run();
```

The endpoint asks the catalog for the index it needs. An index is **not**
registered and **not** injected: the container does not describe indexes, so
there is one way into them and it is the catalog.

`AddLui` has a second shape, for a path that comes from the
configuration:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLui(
    sp => LuiCatalog.OpenOrCreate(sp.GetRequiredService<IConfiguration>().GetValue<string>("App:Path")!));

var app = builder.Build();
app.Run();
```

> **Why so?**
> A container that registered the indexes would have to know their names, and
> the migration that creates them would have to agree with it. Then a new
> index is a change in two places, and a name that is wrong in one of them is
> an exception at run time. One catalog, asked for what it needs, cannot
> drift. See ADR-0033 and ADR-0034.
>
> The factory is the caller's: the container holds the catalog and nothing
> else, and the way the catalog is opened travels with the call that opens
> it (ADR-0066).
## The catalog from the container

The catalog is a singleton, and the application asks it for its indexes. The
index it hands out is a view on the file: it writes when the file allows it.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordmann.LUI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"));

using var host = builder.Build();
host.Start();

// The catalog is injected — the index is asked for.
var catalog = host.Services.GetRequiredService<LuiCatalog>();
var tags = catalog.Index("tags");

Console.WriteLine(tags.Search("veg").Hits[0].Text);
```

```text
vegan
```

The index is created in the migration of the application, and opened in the
runtime path. Both calls are on the catalog:

```csharp
using Nordmann.LUI;

// The migration of the application creates the index.
using var catalog = LuiCatalog.OpenOrCreate("app.db");

var tags = catalog.Create("tags", index => index.SingleWord());
tags.Upsert(1, "vegan", weight: 120);
tags.Upsert(2, "vegetarisch", weight: 80);
```

## Only reading

Sometimes an application may not write: a replica, a file that is read-only, a
service that is not allowed to touch the data. The **file** decides what may
happen — not the index, and not the code.

```csharp
using Nordmann.LUI;

// The file decides what may happen — not the index.
using var catalog = LuiCatalog.OpenReadOnly("app.db");

var tags = catalog.Index("tags");

Console.WriteLine(tags.Search("veg").Hits[0].Text);

try
{
    tags.Upsert(99, "not allowed");
}
catch (LuiReadOnlyException e)
{
    Console.WriteLine($"refused: {e.Message}");
}
```

```text
vegan
refused: The catalog '/srv/app/app.db' was opened with OpenReadOnly — writing an entry (index 'tags') is not allowed. Open it with LuiCatalog.Open or LuiCatalog.OpenOrCreate to write.
```

The same application code therefore runs in the service that writes and in the
service that only reads: `LuiCatalog.OpenOrCreate("app.db")` and
`LuiCatalog.OpenReadOnly("app.db")`. A read-only catalog
writes nothing at all — no write-ahead logging, no `lui_meta` — and every
write is refused with a `LuiReadOnlyException`.

## Starting up: `Initialize()`, the outbox loop and the host pattern

An application has four start steps to know and to order: the triggers and the first sync of a linked index, the rows of the outbox, and the typo
dictionaries — and every forgotten one fails silently (an index without its dictionary never corrects). **`catalog.Initialize()`** does all of them in one
explicit, synchronous call, in LUI's order and by the state of each index: the triggers first, then the first syncs, then the outbox, and last the
dictionaries, one after the other. A second call does nothing, one failing index does not stop the others (a `LuiInitializeException` at the end carries the
report and the error of each), and `NeedsInitialization` on `catalog.Indexes()` says what is still missing — so a readiness check is one line.

The factory of `AddLui` only **opens** the catalog. The indexes are created and initialized after `Build()`, so that a configuration error stops the start at
a line of `Program.cs` — and the initialization runs in the background, in a service of your own, so that the host is up at once and a long first sync does not fail
a liveness probe:

```text
builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"));    // open only
builder.Services.AddLuiOutbox(TimeSpan.FromSeconds(10));              // the one background loop: ApplyPending() every 10 s
builder.Services.AddHostedService<SearchInitializer>();               // your own: catalog.Initialize(cancellationToken: stoppingToken)

var app = builder.Build();
app.Services.GetRequiredService<LuiCatalog>().Create(SearchIndexes.Recipes, SearchIndexes.Tags);   // fast; a broken definition stops the start here
app.MapHealthChecks("/ready");      // not ready while an index has NeedsInitialization != None
app.Run();
```

`AddLuiOutbox` applies the outbox of your linked indexes every `Interval` and stops with the host; a round that fails is logged as a warning and retried with
backoff up to `MaxBackoff`, and on a read-only catalog the host does not start. It does nothing at the start — that is `Initialize()`. **Repeat `Initialize()` until the report says `Ready`**: it is idempotent, and a call ends "not ready" when another replica runs the first sync of a linked index (`InitialSyncRunningElsewhere`) or the lease of a killed one has not run out yet (a minute); the next call does the rest. An exception that escapes the initializer stops the host — catch and log a `LuiInitializeException` to stay up. The sections of the
[README](../../README.md#starting-up-initialize-and-the-host-pattern) and of the [API page](../api.md#initializing--the-start-of-a-process) have the whole pattern;
what follows is the way of `0.3.0`, which `Initialize()` and `AddLuiOutbox` replace.

## The background work is yours — or LUI's, if you ask

Two things repeat in every application that searches with LUI, and both are
the application's business:

- **The warm-up of the typo dictionary.** `WarmUpTypoDictionary()` at the
  start builds it — a query never does. Until it is built, a query that would
  need a correction answers without it and says `CorrectionSkipped`.
- **The outbox of a linked index.** `ApplyPending()` brings the rows the
  triggers noted into the index — after your writes, or on a timer.

The hosting package offers both as one service — **off by default**. The host
asks for it, and the library starts nothing on its own:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordmann.LUI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"));

// The dictionaries are built while the host starts; the outbox is applied every 30 seconds.
builder.Services.AddLuiWarmup(options => options.TypoDictionaryIndexes.Add("tags"));

using var host = builder.Build();
host.Run();
```

`AddLuiWarmup()` registers a service of the host: the host starts it, stops it
and disposes it. The typo dictionaries of your indexes are built at the start —
after the outbox, so that the dictionary knows the words of the rows that came
in — and the outbox of every linked index is applied on an interval (30 s by
default, `options.OutboxInterval = TimeSpan.FromSeconds(5)` says otherwise).

- **Name the indexes that correct typos.** A dictionary costs memory — 194 MB
  for 685,829 words. `TypoDictionaryIndexes` names the indexes that get one;
  left empty, every index does. `WarmUpTypoDictionaries = false` leaves only
  the outbox to the service.
- **The host waits for the dictionaries.** It is ready when they are built —
  seconds for a large index — so that the first query already corrects.
- **The logger of the host hears what happened**: the dictionaries it built,
  and a round of the outbox that failed (a `Warning`, with the exception). The
  rows of a failed round stay in the outbox; the next round tries again.

**The rebuild stays yours.** A dictionary only grows: what was removed lingers
in it until `RebuildTypoDictionary()` (ADR-0025). Only your application knows
when a phase is quiet enough for it.

Without the call, nothing of LUI runs in the background: the container holds
the catalog, and the application asks it for what it needs.


## What you have now

- A catalog in the container, and an index asked for where it is needed.
- An endpoint that answers a search.
- A read-only mode for the services that must not write.
- The background work in the application, where it belongs.

That is the whole road: from an empty file to a search that answers. The
[API](../../docs/api.md) says what every call does, the
[benchmarks](../../docs/benchmarks.md) hold every number, and the
[sample](../../samples/Lui.Sample/) is the application behind this tutorial
— with a page you can click through.
