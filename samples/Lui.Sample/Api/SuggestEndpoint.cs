using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// <c>GET /api/suggest/{index}?q=…</c> — the query API of LUI, every setting of a query as a parameter. The call is
/// synchronous on the request thread (1–3 ms, CPU-bound; the plan's D9), and the request's token reaches the library,
/// so a page that aborts its request stops the query.
/// </summary>
internal static class SuggestEndpoint
{
    internal static void Map(IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/suggest/{index}", async (string index, HttpContext http, SampleIndexes indexes, RequestStats stats) =>
        {
            if (!indexes.TryGet(index, out var sample))
            {
                return Problems.NotFound($"There is no index '{index}'.");
            }

            SuggestParameters parameters;
            try
            {
                parameters = SuggestParameters.From(http.Request.Query);
            }
            catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
            {
                return Problems.BadRequest(e.Message);
            }

            try
            {
                // demo only: a slow server, so that an abort of the page can be seen (and tested)
                if (parameters.DelayMs > 0)
                {
                    await Task.Delay(parameters.DelayMs, http.RequestAborted).ConfigureAwait(false);
                }

                var response = sample.Suggest(parameters.Text, parameters.Options, http.RequestAborted);
                stats.CountServed();
                return Results.Ok(response);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                stats.CountAborted();
                return Results.Empty;
            }
        });
}
