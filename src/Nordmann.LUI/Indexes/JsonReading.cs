using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordmann.LUI.Indexes;

/// <summary>RED: the untyped shape that #268 removed — the index layer deserializes with a JSON context.</summary>
internal static class JsonReading
{
    internal static object? Read(byte[] bytes, Type type, JsonSerializerContext context) => JsonSerializer.Deserialize(bytes, type, context);
}
