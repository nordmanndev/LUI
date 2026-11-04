using System.Diagnostics;
using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// The rest of the public surface of LUI behind routes (LUI-Spezifikation §12): the indexes with their definitions, the
/// write side of an embedded index (<c>Upsert</c>, <c>UpdateWeight</c>, <c>UpdatePriority</c>, <c>Remove</c>), the
/// application's table behind a linked one, and the maintenance (<c>Sync</c>, <c>Check</c>, <c>Rebuild</c>,
/// <c>Optimize</c>, the typo dictionary, the outbox and the triggers).
/// </summary>
internal static class IndexEndpoints
{
    internal static void Map(IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api");
        MapMeta(api);

        var index = api.MapGroup("/indexes/{name}");
        index.MapGet("", (string name, SampleIndexes indexes) => With(indexes, name, sample => Results.Ok(sample.Describe())));
        MapEmbedded(index);
        MapLinked(index);
        MapMaintenance(index);
    }

    private static void MapMeta(RouteGroupBuilder api)
    {
        api.MapGet("/meta", () => Results.Ok(new
        {
            rank = Enum.GetNames<LuiRankingOrder>(),
            priorityMode = Enum.GetNames<LuiPriorityMode>(),
            priority = Enum.GetNames<LuiPriority>(),
            similarity = SuggestParameters.SimilarityNames,
            limits = new { take = LuiQueryOptions.MaxResultsLimit, candidates = LuiQueryOptions.CandidatesLimit, delayMs = SuggestParameters.MaxDelayMs },
            defaults = new LuiQueryOptions(),
        }));

        api.MapGet("/indexes", (SampleIndexes indexes) => Results.Ok(indexes.All.Select(index => index.Describe())));
    }

    /// <summary>The write side of an embedded index: the entries.</summary>
    private static void MapEmbedded(RouteGroupBuilder index)
    {
        index.MapPost("/entries", (string name, UpsertRequest request, SampleIndexes indexes) =>
            Embedded(indexes, name, embedded => Results.Ok(new { key = embedded.Upsert(request) })));

        index.MapPatch("/entries/{key:long}", (string name, long key, PatchRequest request, SampleIndexes indexes) =>
            Embedded(indexes, name, embedded => embedded.Patch(key, request)
                ? Results.Ok(new { key })
                : Problems.NotFound($"There is no entry {key} in '{name}'.")));

        index.MapDelete("/entries/{key:long}", (string name, long key, SampleIndexes indexes) =>
            Embedded(indexes, name, embedded => embedded.Remove(key)
                ? Results.NoContent()
                : Problems.NotFound($"There is no entry {key} in '{name}'.")));

        index.MapPost("/rebuild", (string name, SampleIndexes indexes) =>
            Embedded(indexes, name, embedded => Operation("Rebuild", () =>
            {
                embedded.Rebuild();
                return null;
            })));
    }

    /// <summary>The application's table behind a linked index, and telling the index about it.</summary>
    private static void MapLinked(RouteGroupBuilder index)
    {
        index.MapPost("/rows", (string name, TagRowRequest request, SampleIndexes indexes) =>
            Linked(indexes, name, linked => Results.Ok(new { id = linked.UpsertRow(request) })));

        index.MapDelete("/rows/{id:long}", (string name, long id, SampleIndexes indexes) =>
            Linked(indexes, name, linked => linked.DeleteRow(id)
                ? Results.NoContent()
                : Problems.NotFound($"There is no row {id} in the table of '{name}'.")));

        index.MapPost("/refresh", (string name, RefreshRequest request, SampleIndexes indexes, CancellationToken token) =>
            Linked(indexes, name, linked => Operation("RefreshMany", () => linked.Refresh(request.Keys, token))));

        index.MapPost("/apply-pending", (string name, SampleIndexes indexes, CancellationToken token) =>
            Linked(indexes, name, linked => Operation("ApplyPending", () => linked.ApplyPending(token))));

        index.MapPost("/triggers", (string name, TriggersRequest request, SampleIndexes indexes) =>
            Linked(indexes, name, linked => Operation(request.Create ? "CreateTriggers" : "DropTriggers", () =>
            {
                linked.SetTriggers(request.Create);
                return linked.Describe().Triggers;
            })));
    }

    /// <summary>What both kinds have: sync, check, optimize and the typo dictionary.</summary>
    private static void MapMaintenance(RouteGroupBuilder index)
    {
        index.MapPost("/sync", (string name, SampleIndexes indexes, CancellationToken token) =>
            With(indexes, name, sample => Operation("Sync", () => sample.Sync(token))));

        index.MapGet("/check", (string name, string? source, SampleIndexes indexes, CancellationToken token) =>
            With(indexes, name, sample => string.Equals(source, "seed", StringComparison.Ordinal) && sample is IEmbeddedSampleIndex embedded
                ? Operation("Check(entries)", () => embedded.CheckAgainstSeed(token))
                : Operation("Check()", sample.Check)));

        index.MapPost("/optimize", (string name, SampleIndexes indexes) =>
            With(indexes, name, sample => Operation("Optimize", () =>
            {
                sample.Optimize();
                return null;
            })));

        index.MapPost("/typos/warm-up", (string name, SampleIndexes indexes, CancellationToken token) =>
            With(indexes, name, sample => Operation("WarmUpTypoDictionary", () =>
            {
                sample.WarmUpTypoDictionary(token);
                return sample.Describe().TypoDictionaryState;
            })));

        index.MapPost("/typos/rebuild", (string name, SampleIndexes indexes, CancellationToken token) =>
            With(indexes, name, sample => Operation("RebuildTypoDictionary", () =>
            {
                sample.RebuildTypoDictionary(token);
                return sample.Describe().TypoDictionaryState;
            })));
    }

    private static IResult With(SampleIndexes indexes, string name, Func<ISampleIndex, IResult> action) =>
        indexes.TryGet(name, out var sample) ? Guard(() => action(sample)) : Problems.NotFound($"There is no index '{name}'.");

    private static IResult Embedded(SampleIndexes indexes, string name, Func<IEmbeddedSampleIndex, IResult> action) =>
        With(indexes, name, sample => sample is IEmbeddedSampleIndex embedded
            ? action(embedded)
            : Problems.Conflict($"'{name}' is a linked index: its text lives in the application's table. Write to /rows, then tell the index."));

    private static IResult Linked(SampleIndexes indexes, string name, Func<ILinkedSampleIndex, IResult> action) =>
        With(indexes, name, sample => sample is ILinkedSampleIndex linked
            ? action(linked)
            : Problems.Conflict($"'{name}' is an embedded index: its entries live in LUI's table. Write to /entries."));

    private static IResult Operation(string operation, Func<object?> action)
    {
        var clock = Stopwatch.StartNew();
        var report = action();
        return Results.Ok(new OperationResult(operation, clock.Elapsed.TotalMilliseconds, report));
    }

    /// <summary>A wrong argument is the caller's (400); a condition LUI reports — a definition that does not match, a table that is gone — is a conflict (409).</summary>
    private static IResult Guard(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException e)
        {
            return Problems.BadRequest(e.Message);
        }
        catch (LuiException e)
        {
            return Problems.Conflict(e.Message);
        }
        catch (InvalidOperationException e)
        {
            return Problems.Conflict(e.Message);
        }
    }
}
