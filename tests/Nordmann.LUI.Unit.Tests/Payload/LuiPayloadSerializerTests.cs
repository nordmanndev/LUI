using System.Text.Json;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Payload;

/// <summary>
/// The JSON serializer LUI brings (ADR-0035, ADR-0072): one per payload type, out of the source-generated context of the
/// application — it writes a payload of its type, reads it back strictly, and says at once when the context does not know the type.
/// </summary>
public sealed class LuiPayloadSerializerTests
{
    private static readonly ILuiPayloadSerializer<Chip> Serializer = LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default);

    [Fact]
    public void A_payload_survives_the_round_trip()
    {
        var bytes = Serializer.Serialize(new Chip("vegan", "green"));

        Serializer.Deserialize(bytes).ShouldBe(new Chip("vegan", "green"));
    }

    [Fact]
    public void The_payload_is_utf8_json()
    {
        var bytes = Serializer.Serialize(new Chip("vegan", "grün"));

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
        var bytes = Serializer.Serialize(new Chip("vegan"));

        Serializer.Deserialize(bytes).ShouldBe(new Chip("vegan", null));
    }

    [Fact]
    public void The_context_is_required()
    {
        Should.Throw<ArgumentNullException>(() => LuiPayloadSerializer.Json<Chip>(null!));
    }

    [Fact]
    public void A_payload_is_written_as_the_type_of_the_serializer()
    {
        // the serializer is bound to one type (ADR-0072): the bytes are those of the context for that type
        Serializer.Serialize(new Chip("vegan", "green")).ShouldBe(JsonSerializer.SerializeToUtf8Bytes(new Chip("vegan", "green"), PayloadJsonContext.Default.Chip));
    }

    [Fact]
    public void A_type_that_is_not_in_the_context_is_refused_when_the_serializer_is_made_and_named()
    {
        // the untyped serializer refused it on the first write or read; the serializer of a type refuses it at once (#260)
        var exception = Should.Throw<LuiConfigurationException>(() => LuiPayloadSerializer.Json<Uri>(PayloadJsonContext.Default));

        exception.Message.ShouldContain("System.Uri");
        exception.Message.ShouldContain(nameof(PayloadJsonContext));
    }

    [Fact]
    public void A_payload_read_as_a_type_of_another_shape_is_refused_not_filled_with_defaults()
    {
        // review of PR 60, I1: JSON is tolerant — without the strict reading a chip read as a recipe came back as
        // Recipe { Title = "", Minutes = 0 }, wrong data that looks like right data
        var bytes = Serializer.Serialize(new Chip("vegan", "green"));

        var exception = Should.Throw<JsonException>(() => LuiPayloadSerializer.Json<Recipe>(PayloadJsonContext.Default).Deserialize(bytes));

        exception.Message.ShouldContain("Slug");
    }

    [Fact]
    public void A_payload_that_lacks_a_parameter_of_the_constructor_is_refused()
    {
        var bytes = LuiPayloadSerializer.Json<Named>(PayloadJsonContext.Default).Serialize(new Named("vegan"));

        Should.Throw<JsonException>(() => LuiPayloadSerializer.Json<Ranked>(PayloadJsonContext.Default).Deserialize(bytes)).Message.ShouldContain("Rank");
    }

    [Fact]
    public void A_type_with_every_member_of_the_bytes_and_optional_ones_besides_is_read()
    {
        // the boundary of the strict reading: nothing in the bytes is lost, and what the type adds has a default
        var bytes = LuiPayloadSerializer.Json<Named>(PayloadJsonContext.Default).Serialize(new Named("vegan"));

        Serializer.Deserialize(bytes).ShouldBe(new Chip("vegan"));
    }

    [Fact]
    public void The_json_value_null_is_refused()
    {
        // A stored null is not a payload — it is a hole in the database. The serializer says so instead of handing out a default
        // value that the caller would read as data.
        var exception = Should.Throw<JsonException>(
            () => Serializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes<Chip?>(null, PayloadJsonContext.Default.Chip)));

        exception.Message.ShouldContain("null");
        Should.Throw<JsonException>(() => Serializer.Deserialize("null"u8)).Message.ShouldContain("null");
    }

    [Fact]
    public void The_json_serializer_has_the_format_json_strict_v1()
    {
        Serializer.Format.ShouldBe("json-strict-v1");
    }
}
