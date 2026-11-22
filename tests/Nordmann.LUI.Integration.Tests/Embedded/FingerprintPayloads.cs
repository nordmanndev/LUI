using System.Text;
using System.Text.Json.Serialization;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>A payload that the JSON context of the chips does not know — and that a serializer of its own writes.</summary>
/// <param name="Text">The text of the note.</param>
public sealed record Note(string Text);

/// <summary>A payload whose name the attribute fixes: the class is renamed in <see cref="NewRecipe"/>, the stored name stays.</summary>
/// <param name="Title">The title.</param>
[LuiPayloadName("recipe")]
public sealed record OldRecipe(string Title);

/// <summary>The same payload after a rename of the class: the same name, from the same attribute.</summary>
/// <param name="Title">The title.</param>
[LuiPayloadName("recipe")]
public sealed record NewRecipe(string Title);

/// <summary>A type that is not known to any context of the tests.</summary>
/// <param name="Value">The value.</param>
public sealed record Unknown(string Value);

/// <summary>The namespace of the first of two payload types that differ only in it.</summary>
public static class Alpha
{
    /// <summary>A payload named like the one of <see cref="Beta"/>.</summary>
    /// <param name="Value">The value.</param>
    public sealed record Same(string Value);
}

/// <summary>The namespace of the second of two payload types that differ only in it.</summary>
public static class Beta
{
    /// <summary>A payload named like the one of <see cref="Alpha"/>.</summary>
    /// <param name="Value">The value.</param>
    public sealed record Same(string Value);
}

/// <summary>The JSON context of the payloads of these tests.</summary>
[JsonSerializable(typeof(OldRecipe))]
[JsonSerializable(typeof(NewRecipe))]
[JsonSerializable(typeof(Alpha.Same), TypeInfoPropertyName = "AlphaSame")]
[JsonSerializable(typeof(Beta.Same), TypeInfoPropertyName = "BetaSame")]
public sealed partial class FingerprintJsonContext : JsonSerializerContext;

/// <summary>A second JSON context that knows the chip — together with <see cref="ChipJsonContext"/> an ambiguity that the registry refuses.</summary>
[JsonSerializable(typeof(Chip))]
public sealed partial class AnotherChipContext : JsonSerializerContext;

/// <summary>A serializer of another format than JSON for the chips: two lines of text. A format of its own, and it counts.</summary>
/// <param name="format">The format — what is stored and compared.</param>
public sealed class LinesChipSerializer(string format = "lines-v1") : ILuiPayloadSerializer<Chip>
{
    /// <inheritdoc />
    public string Format { get; } = format;

    /// <summary>How often a payload was read.</summary>
    public int Deserialized { get; private set; }

    /// <inheritdoc />
    public byte[] Serialize(Chip payload) => Encoding.UTF8.GetBytes($"{payload.Slug}\n{payload.Color}");

    /// <inheritdoc />
    public Chip Deserialize(ReadOnlySpan<byte> bytes)
    {
        Deserialized++;
        var parts = Encoding.UTF8.GetString(bytes).Split('\n');
        return new Chip(parts[0], parts[1].Length == 0 ? null : parts[1]);
    }
}

/// <summary>A serializer of the notes: the text behind a marker. Not JSON.</summary>
public sealed class NoteSerializer : ILuiPayloadSerializer<Note>
{
    /// <inheritdoc />
    public string Format => "note-text-v1";

    /// <inheritdoc />
    public byte[] Serialize(Note payload) => Encoding.UTF8.GetBytes("NOTE:" + payload.Text);

    /// <inheritdoc />
    public Note Deserialize(ReadOnlySpan<byte> bytes) => new(Encoding.UTF8.GetString(bytes)["NOTE:".Length..]);
}
