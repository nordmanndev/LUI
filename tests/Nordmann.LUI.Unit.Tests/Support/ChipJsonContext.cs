using System.Text.Json.Serialization;

namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>The source-generated JSON context of <see cref="Chip"/> and of the payloads of other shapes.</summary>
[JsonSerializable(typeof(Chip))]
[JsonSerializable(typeof(Recipe))]
[JsonSerializable(typeof(Named))]
[JsonSerializable(typeof(Ranked))]
public sealed partial class ChipJsonContext : JsonSerializerContext;
