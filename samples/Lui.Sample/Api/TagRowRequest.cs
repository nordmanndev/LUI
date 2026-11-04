namespace Nordmann.LUI.Sample.Api;

/// <summary>The body of <c>POST /api/indexes/{name}/rows</c> (a linked index): a row of the application's own table.</summary>
internal sealed record TagRowRequest(long? Id, string Name, string Kind = "Sonstiges", string Color = "#718096", int UsageCount = 0, int Rank = 3);
