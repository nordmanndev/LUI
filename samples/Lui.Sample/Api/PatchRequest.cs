namespace Nordmann.LUI.Sample.Api;

/// <summary>The body of <c>PATCH /api/indexes/{name}/entries/{key}</c>: only what is given changes.</summary>
internal sealed record PatchRequest(int? Weight, LuiPriority? Priority);
