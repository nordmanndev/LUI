using System.Text.Json.Serialization;

// The sample wires the pieces together; the library does the work.
// See docs/plan/2026-11-02-lui-repo-plan.md — the endpoints below grow into
// the full surface (suggest, index management, sync/check/rebuild) as the
// library features land (PR-003 … PR-011).
var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/indexes", () => Results.Ok(Array.Empty<object>()));

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration tests can host the sample.</summary>
public partial class Program;
