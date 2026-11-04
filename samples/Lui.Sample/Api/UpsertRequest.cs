using System.Text.Json;

namespace Nordmann.LUI.Sample.Api;

/// <summary>The body of <c>POST /api/indexes/{name}/entries</c> (an embedded index).</summary>
internal sealed record UpsertRequest(long? Key, string Text, int Weight = 0, LuiPriority Priority = LuiPriority.Normal, JsonElement? Payload = null);
