using System.Text.Json.Serialization;

namespace Nordmann.LUI.Indexing;

/// <summary>Source-generated JSON for the definition stored in <c>lui_meta</c>: no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(IndexDefinition))]
internal sealed partial class LuiJsonContext : JsonSerializerContext;
