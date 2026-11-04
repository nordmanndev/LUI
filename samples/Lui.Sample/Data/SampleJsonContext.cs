using System.Text.Json.Serialization;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The source-generated JSON metadata of the stored payload (no reflection, ready for trimming). camelCase, as the page
/// speaks it, and read case-insensitively: a payload that arrives as <c>{"category": …}</c> must not turn into an empty record.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RecipePayload))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
