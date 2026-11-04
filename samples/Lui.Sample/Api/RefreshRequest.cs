namespace Nordmann.LUI.Sample.Api;

/// <summary>The body of <c>POST …/refresh</c>.</summary>
internal sealed record RefreshRequest(IReadOnlyList<long> Keys);
