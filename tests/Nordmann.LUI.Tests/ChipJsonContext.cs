using System.Text.Json.Serialization;

namespace Nordmann.LUI.Tests;

[JsonSerializable(typeof(Chip))]
internal sealed partial class ChipJsonContext : JsonSerializerContext;
