using System.Text.Json.Serialization;

namespace Nordmann.LUI.Testing;

[JsonSerializable(typeof(Chip))]
public sealed partial class ChipJsonContext : JsonSerializerContext;
