using System.Text.Json;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Payload;

/// <summary>
/// The JSON serialiser LUI brings (ADR-0035): one per catalog, it knows no type in advance — it writes a payload with the
/// type it has and reads it with the type the caller asks for, both out of the source-generated context of the
/// application.
/// </summary>
public sealed class LuiPayloadSerializerTests
{
    private static readonly ILuiPayloadSerializer Serializer = LuiPayloadSerializer.Json(PayloadJsonContext.Default);

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
        exception.Message.ShouldContain(nameof(PayloadJsonContext));
    }

    [Fact]
    public void A_type_that_is_not_in_the_context_is_refused_on_the_read_and_named()
    {
        var bytes = Serializer.Serialize(new Chip("vegan"));

        Should.Throw<NotSupportedException>(() => Serializer.Deserialize<Uri>(bytes)).Message.ShouldContain("System.Uri");
    }

    [Fact]
    public void A_payload_read_as_a_type_of_another_shape_is_refused_not_filled_with_defaults()
    {
        // review of PR 60, I1: JSON is tolerant — without the strict reading a chip read as a recipe came back as
        // Recipe { Title = "", Minutes = 0 }, wrong data that looks like right data
        var bytes = Serializer.Serialize(new Chip("vegan", "green"));

        var exception = Should.Throw<JsonException>(() => Serializer.Deserialize<Recipe>(bytes));

        exception.Message.ShouldContain("Slug");
    }

    [Fact]
    public void A_payload_that_lacks_a_parameter_of_the_constructor_is_refused()
    {
        var bytes = Serializer.Serialize(new Named("vegan"));

        Should.Throw<JsonException>(() => Serializer.Deserialize<Ranked>(bytes)).Message.ShouldContain("Rank");
    }

    [Fact]
    public void A_type_with_every_member_of_the_bytes_and_optional_ones_besides_is_read()
    {
        // the boundary of the strict reading: nothing in the bytes is lost, and what the type adds has a default
        var bytes = Serializer.Serialize(new Named("vegan"));

        Serializer.Deserialize<Chip>(bytes).ShouldBe(new Chip("vegan"));
    }

    [Fact]
    public void The_json_value_null_is_refused()
    {
        // A stored null is not a payload — it is a hole in the database. The
        // serialiser says so instead of handing out a default value that the
        // caller would read as data.
        var exception = Should.Throw<JsonException>(
            () => Serializer.Deserialize<Chip>(JsonSerializer.SerializeToUtf8Bytes<Chip?>(null, PayloadJsonContext.Default.Chip)));

        exception.Message.ShouldContain("null");
    }

    [Fact]
    public void The_typed_json_serializer_has_the_format_json_strict_v1()
    {
        var serializer = LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default);

        serializer.Format.ShouldBe("json-strict-v1");
        serializer.ShouldBeAssignableTo<ILuiPayloadSerializer<Chip>>();
    }

    [Fact]
    public void A_typed_payload_survives_the_round_trip_and_is_the_same_json_as_the_untyped_serializer_writes()
    {
        var typed = LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default);

        var bytes = typed.Serialize(new Chip("vegan", "green"));

        typed.Deserialize(bytes).ShouldBe(new Chip("vegan", "green"));
        bytes.ShouldBe(Serializer.Serialize(new Chip("vegan", "green")), "one wire format: the bytes of a typed and of an untyped catalog are the same");
        typed.Deserialize(Serializer.Serialize(new Chip("vegan"))).ShouldBe(new Chip("vegan"));
    }

    [Fact]
    public void The_typed_serializer_reads_strictly()
    {
        var bytes = LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default).Serialize(new Chip("vegan", "green"));

        Should.Throw<JsonException>(() => LuiPayloadSerializer.Json<Recipe>(PayloadJsonContext.Default).Deserialize(bytes)).Message.ShouldContain("Slug");
        var named = LuiPayloadSerializer.Json<Named>(PayloadJsonContext.Default).Serialize(new Named("vegan"));
        Should.Throw<JsonException>(() => LuiPayloadSerializer.Json<Ranked>(PayloadJsonContext.Default).Deserialize(named)).Message.ShouldContain("Rank");
        LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default).Deserialize(named).ShouldBe(new Chip("vegan"));
    }

    [Fact]
    public void The_typed_serializer_refuses_the_json_value_null()
    {
        var serializer = LuiPayloadSerializer.Json<Chip>(PayloadJsonContext.Default);

        Should.Throw<JsonException>(() => serializer.Deserialize("null"u8)).Message.ShouldContain("null");
    }

    [Fact]
    public void The_typed_serializer_of_a_type_that_is_not_in_the_context_is_a_configuration_error_and_names_both()
    {
        var exception = Should.Throw<LuiConfigurationException>(() => LuiPayloadSerializer.Json<Uri>(PayloadJsonContext.Default));

        exception.Message.ShouldContain("System.Uri");
        exception.Message.ShouldContain(nameof(PayloadJsonContext));
    }

    [Fact]
    public void The_context_is_required_for_the_typed_serializer()
    {
        Should.Throw<ArgumentNullException>(() => LuiPayloadSerializer.Json<Chip>(null!));
    }
}
