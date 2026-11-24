using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordmann.LUI.Indexes;

/// <summary>RED: the serializer call is nested in an internal type — NetArchTest's namespace selection does not see it.</summary>
internal sealed class NestedReading
{
    internal static class Inner
    {
        internal static object? Read(byte[] bytes, Type type, JsonSerializerContext context) => JsonSerializer.Deserialize(bytes, type, context);
    }
}
