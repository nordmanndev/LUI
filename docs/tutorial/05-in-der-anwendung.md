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
builder.Services.AddLui("Data Source=app.db");

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

`AddLui` has a second shape, for a connection string that comes from the
configuration:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLui(
    sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("app")!);

var app = builder.Build();
app.Run();
```

> **Why so?**
> A container that registered the indexes would have to know their names, and
> the migration that creates them would have to agree with it. Then a new
> index is a change in two places, and a name that is wrong in one of them is
> an exception at run time. One catalog, asked for what it needs, cannot
> drift. See
> [ADR-0033](../../docs/adr/0033-the-read-path-opens-an-index-without-writing.md)
> and
> [ADR-0034](../../docs/adr/0034-the-hosting-package-carries-only-the-dependency-injection.md).

## The catalog from the container

The catalog is a singleton, and the application asks it for its indexes. The
index it hands out is a view on the file: it writes when the file allows it.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordmann.LUI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLui("Data Source=app.db");

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
using var catalog = LuiCatalog.Open("Data Source=app.db");

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
using var catalog = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");

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
refused: The catalog is read-only: the database was opened with Mode=ReadOnly, so writing an entry is refused. Open the catalog with a connection string that may write (Data Source=app.db) to write; a read-only catalog reads and searches.
```

The same application code therefore runs in the service that writes and in the
service that only reads: `LuiCatalog.Open("Data Source=app.db")` and
`LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly")`. A read-only catalog
writes nothing at all — no write-ahead logging, no `lui_meta` — and every
write is refused with a `LuiReadOnlyException`.

## The background work is yours

The hosting package brings the dependency injection and nothing else. Two
things stay in your application, because they are your application's:

- **The warm-up of the typo dictionary.** `WarmUpTypoDictionary()` at the
  start builds it, so that the first typo does not pay for it. No query waits
  for a warm-up: a query that would need a correction meanwhile answers
  without it and says `CorrectionSkipped`.
- **The outbox of a linked index.** `ApplyPending()` brings the rows the
  triggers noted into the index — after your writes, or on a timer.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordmann.LUI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLui("Data Source=app.db");

// The application keeps its own index current — the container does not.
builder.Services.AddHostedService<OutboxWorker>();

using var host = builder.Build();
host.Run();

/// <summary>Brings the noted rows of the linked index into the index, every five seconds.</summary>
internal sealed class OutboxWorker(LuiCatalog catalog) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            catalog.LinkedIndex("articles").ApplyPending(stoppingToken);
        }
    }
}
```

## What you have now

- A catalog in the container, and an index asked for where it is needed.
- An endpoint that answers a search.
- A read-only mode for the services that must not write.
- The background work in the application, where it belongs.

That is the whole road: from an empty file to a search that answers. The
[specification](../../docs/spec/LUI-Spezifikation.md) is the contract behind
every step, the [benchmarks](../../docs/benchmarks.md) hold every number, and
the [sample](../../samples/Lui.Sample/) is the application behind this tutorial
— with a page you can click through.
