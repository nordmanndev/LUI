namespace Nordmann.LUI.Sample.Api;

/// <summary>The body of <c>POST /api/indexes/{name}/fill</c>: how many entries the index should hold.</summary>
internal sealed record FillRequest(int Count);
