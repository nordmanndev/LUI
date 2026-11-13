using System.Text.Json.Serialization;

namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>
/// The source-generated JSON context of the payloads of the unit tests: the chip of the
/// infrastructure (ADR-0058) and the payloads of other shapes.
/// </summary>
[JsonSerializable(typeof(Chip))]
[JsonSerializable(typeof(Recipe))]
[JsonSerializable(typeof(Named))]
[JsonSerializable(typeof(Ranked))]
public sealed partial class PayloadJsonContext : JsonSerializerContext;
