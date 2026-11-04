namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// What a hit of <c>tags</c> carries: read from two columns of the application's own <c>tag</c> table (a linked
/// index with <c>WithPayload(columns, map)</c>, ADR-0015) — LUI stores nothing of it.
/// </summary>
internal sealed record TagPayload(string Kind, string Color);
