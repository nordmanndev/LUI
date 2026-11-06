using System.Text.Json.Serialization;

namespace Nordmann.LUI.Testing;

[JsonSerializable(typeof(Chip))]
[JsonSerializable(typeof(RecipeCard))]
public sealed partial class ChipJsonContext : JsonSerializerContext;
