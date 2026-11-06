using System.Text.Json;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The JSON serialiser LUI brings (ADR-0035): one per catalog, it knows no type in advance — it writes a payload with the
/// type it has and reads it with the type the caller asks for, both out of the source-generated context of the
/// application.
/// </summary>
public sealed class LuiPayloadSerializerTests
{
    private static readonly ILuiPayloadSerializer Serializer = LuiPayloadSerializer.Json(ChipJsonContext.Default);

    [Fact]
    public void A_payload_survives_the_round_trip()
    {
        var bytes = Serializer.Serialize(new Chip("vegan", "green"));

        Serializer.Deserialize<Chip>(bytes).ShouldBe(new Chip("vegan", "green"));
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

        Serializer.Deserialize<Chip>(bytes).ShouldBe(new Chip("vegan", null));
    }

    [Fact]
    public void The_context_is_required()
    {
        Should.Throw<ArgumentNullException>(() => LuiPayloadSerializer.Json(null!));
    }

    [Fact]
    public void A_payload_is_written_with_the_type_it_has_at_run_time()
    {
        // the entry holds an object: the serialiser takes the type of the instance, not of the variable
        object payload = new Chip("vegan", "green");

        Serializer.Deserialize<Chip>(Serializer.Serialize(payload)).ShouldBe(new Chip("vegan", "green"));
    }

    [Fact]
    public void A_type_that_is_not_in_the_context_is_refused_on_the_write_and_named()
    {
        var exception = Should.Throw<NotSupportedException>(() => Serializer.Serialize(new Uri("https://example.org")));

        exception.Message.ShouldContain("System.Uri");
        exception.Message.ShouldContain(nameof(ChipJsonContext));
    }

    [Fact]
    public void A_type_that_is_not_in_the_context_is_refused_on_the_read_and_named()
    {
        var bytes = Serializer.Serialize(new Chip("vegan"));

        Should.Throw<NotSupportedException>(() => Serializer.Deserialize<Uri>(bytes)).Message.ShouldContain("System.Uri");
    }

    [Fact]
    public void The_json_value_null_is_refused()
    {
        // A stored null is not a payload — it is a hole in the database. The
        // serialiser says so instead of handing out a default value that the
        // caller would read as data.
        var exception = Should.Throw<JsonException>(
            () => Serializer.Deserialize<Chip>(JsonSerializer.SerializeToUtf8Bytes<Chip?>(null, ChipJsonContext.Default.Chip)));

        exception.Message.ShouldContain("null");
    }
}
