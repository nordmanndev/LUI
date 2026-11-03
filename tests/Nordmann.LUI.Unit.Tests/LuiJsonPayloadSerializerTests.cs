using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The JSON payload serialiser: a public type, and the one the builder hands
/// out for a payload that is a plain object (ADR-0013). It is the path a
/// consumer takes when they do not want to write a serialiser of their own.
/// </summary>
public sealed class LuiJsonPayloadSerializerTests
{
    [Fact]
    public void A_payload_survives_the_round_trip()
    {
        var serializer = new LuiJsonPayloadSerializer<Chip>(ChipJsonContext.Default.Chip);

        var bytes = serializer.Serialize(new Chip("vegan", "green"));

        serializer.Deserialize(bytes).ShouldBe(new Chip("vegan", "green"));
    }

    [Fact]
    public void The_payload_is_utf8_json()
    {
        var serializer = new LuiJsonPayloadSerializer<Chip>(ChipJsonContext.Default.Chip);

        var bytes = serializer.Serialize(new Chip("vegan", "grün"));

        // The bytes are UTF-8 — not UTF-16, not the platform's default. The
        // value itself is escaped by System.Text.Json, which is its contract;
        // what matters is that it comes back as the text that went in.
        var json = JsonDocument.Parse(bytes);
        json.RootElement.GetProperty("Slug").GetString().ShouldBe("vegan");
        json.RootElement.GetProperty("Color").GetString().ShouldBe("grün");
    }

    [Fact]
    public void A_payload_without_a_colour_keeps_the_null()
    {
        var serializer = new LuiJsonPayloadSerializer<Chip>(ChipJsonContext.Default.Chip);

        var bytes = serializer.Serialize(new Chip("vegan"));

        serializer.Deserialize(bytes).ShouldBe(new Chip("vegan", null));
    }

    [Fact]
    public void The_type_information_is_required()
    {
        Should.Throw<ArgumentNullException>(() => new LuiJsonPayloadSerializer<Chip>(null!));
    }

    [Fact]
    public void The_json_value_null_is_refused()
    {
        // A stored null is not a payload — it is a hole in the database. The
        // serialiser says so instead of handing out a default value that the
        // caller would read as data.
        var serializer = new LuiJsonPayloadSerializer<Chip>(ChipJsonContext.Default.Chip);

        var exception = Should.Throw<InvalidOperationException>(
            () => serializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes<Chip?>(null, ChipJsonContext.Default.Chip)));

        exception.Message.ShouldContain("null");
    }
}
