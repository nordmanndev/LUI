using System.Text.Json;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// What the sample knows of the payload of one of its indexes (ADR-0035): LUI's index knows no type, so the application
/// says it where it uses the payload — how a payload of a request is read, and how the payload of a hit is shown.
/// </summary>
/// <param name="Read">Turns the JSON of a request into the payload that is stored.</param>
/// <param name="Show">Reads the payload of a hit, with the type the application knows.</param>
internal sealed record SamplePayload(Func<JsonElement, object> Read, Func<LuiHit, object?> Show);
