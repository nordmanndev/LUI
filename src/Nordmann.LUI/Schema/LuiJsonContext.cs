using System.Text.Json.Serialization;

namespace Nordmann.LUI.Schema;

/// <summary>Source-generated JSON for the definition stored in <c>lui_meta</c>: no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IndexDefinition))]
internal sealed partial class LuiJsonContext : JsonSerializerContext;
