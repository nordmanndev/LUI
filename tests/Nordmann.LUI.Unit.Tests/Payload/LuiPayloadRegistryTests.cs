using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Payload;

/// <summary>
/// The payload registry (ADR-0072): it maps a payload type to the serializer that writes and reads it, with no silent
/// precedence — an explicit <c>Use&lt;T&gt;</c> wins over the contexts, a type that two contexts know is an error that names
/// both, and the catalog works on a frozen copy.
/// </summary>
public sealed class LuiPayloadRegistryTests
{
    [Fact]
    public void The_registry_returns_the_serializer_of_a_type()
    {
        var registry = new LuiPayloadRegistry().Use(PayloadJsonContext.Default);
        var recipes = registry.Freeze();

        var chip = recipes.Resolve<Chip>("chips");
        var recipe = recipes.Resolve<Recipe>("recipes");

        chip.Format.ShouldBe("json-strict-v1");
        recipe.Format.ShouldBe("json-strict-v1");
        chip.TypeName.ShouldBe("Nordmann.LUI.Testing.Chip");
        chip.Serializer.Deserialize(chip.Serializer.Serialize(new Chip("vegan", "green"))).ShouldBe(new Chip("vegan", "green"));
        recipes.Resolve<Chip>("chips").ShouldBeSameAs(chip, "the lookup is made once per type");
    }

    [Fact]
    public void A_serializer_registered_for_a_type_is_the_one_that_comes_back()
    {
        var serializer = new LinesSerializer();
        var frozen = new LuiPayloadRegistry().Use(serializer).Freeze();

        var codec = frozen.Resolve<Chip>("chips");

        codec.Serializer.ShouldBeSameAs(serializer);
        codec.Format.ShouldBe("lines-v1");
    }

    [Fact]
    public void Use_of_T_wins_over_a_context()
    {
        var serializer = new LinesSerializer();

        // the order of the calls decides nothing: the explicit choice wins either way
        var contextFirst = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Use(serializer).Freeze();
        var serializerFirst = new LuiPayloadRegistry().Use(serializer).Use(PayloadJsonContext.Default).Freeze();

        contextFirst.Resolve<Chip>("chips").Serializer.ShouldBeSameAs(serializer);
        serializerFirst.Resolve<Chip>("chips").Serializer.ShouldBeSameAs(serializer);
        contextFirst.Resolve<Recipe>("recipes").Format.ShouldBe("json-strict-v1", "the context still serves the other types");
    }

    [Fact]
    public void A_second_Use_of_the_same_T_throws()
    {
        var first = new LinesSerializer();
        var registry = new LuiPayloadRegistry().Use(first);

        var exception = Should.Throw<LuiConfigurationException>(() => registry.Use(new LinesSerializer()));

        exception.Message.ShouldContain("Nordmann.LUI.Testing.Chip");
        exception.Message.ShouldContain("twice");
        registry.Freeze().Resolve<Chip>("chips").Serializer.ShouldBeSameAs(first, "the first registration stays");
    }

    [Fact]
    public void Two_contexts_that_know_a_type_throw_naming_both()
    {
        var firstThenSecond = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Use(ChipJsonContext.Default).Freeze();
        var secondThenFirst = new LuiPayloadRegistry().Use(ChipJsonContext.Default).Use(PayloadJsonContext.Default).Freeze();

        foreach (var frozen in new[] { firstThenSecond, secondThenFirst })
        {
            var exception = Should.Throw<LuiConfigurationException>(() => frozen.Resolve<Chip>("chips"));

            exception.Message.ShouldContain("'chips'", Case.Sensitive);
            exception.Message.ShouldContain(typeof(PayloadJsonContext).FullName!);
            exception.Message.ShouldContain(typeof(ChipJsonContext).FullName!);
            exception.Message.ShouldContain("Nordmann.LUI.Testing.Chip");
        }

        // a type that only one of them knows is no problem, and an explicit registration resolves the conflict
        firstThenSecond.Resolve<Recipe>("recipes").Format.ShouldBe("json-strict-v1");
        firstThenSecond.Resolve<RecipeCard>("cards").Format.ShouldBe("json-strict-v1");
        new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Use(ChipJsonContext.Default).Use(new LinesSerializer()).Freeze()
            .Resolve<Chip>("chips").Format.ShouldBe("lines-v1");
    }

    [Fact]
    public void A_type_that_no_registration_knows_throws_naming_the_definition_the_type_and_the_way_to_register_it()
    {
        var frozen = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Freeze();

        var exception = Should.Throw<LuiConfigurationException>(() => frozen.Resolve<Uri>("links"));

        exception.Message.ShouldContain("'links'", Case.Sensitive);
        exception.Message.ShouldContain("System.Uri");
        exception.Message.ShouldContain("Use(");
        exception.Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
    }

    [Fact]
    public void A_failure_is_kept_and_a_new_exception_is_thrown_on_every_use()
    {
        var frozen = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Freeze();

        var first = Should.Throw<LuiConfigurationException>(() => frozen.Resolve<Uri>("links"));
        var second = Should.Throw<LuiConfigurationException>(() => frozen.Resolve<Uri>("links"));
        var other = Should.Throw<LuiConfigurationException>(() => frozen.Resolve<Uri>("other_links"));

        second.ShouldNotBeSameAs(first, "one exception object thrown from several threads would have its stack trace overwritten");
        second.Message.ShouldBe(first.Message);
        other.Message.ShouldContain("'other_links'", Case.Sensitive);
        frozen.Lookups.ShouldBe(1, "the failure is cached with the type, not looked up again");
    }

    [Fact]
    public void The_lookup_of_a_type_is_made_once_whatever_asks_for_it()
    {
        var frozen = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Freeze();

        for (var i = 0; i < 5; i++)
        {
            _ = frozen.Resolve<Chip>("chips");
            _ = frozen.Resolve<Chip>("chips_again");
        }

        frozen.Lookups.ShouldBe(1);
        _ = frozen.Resolve<Recipe>("recipes");
        frozen.Lookups.ShouldBe(2);
    }

    [Fact]
    public void A_change_after_the_catalog_was_opened_has_no_effect()
    {
        var registry = new LuiPayloadRegistry().Use(PayloadJsonContext.Default);
        var frozen = registry.Freeze();

        _ = registry.Use(new LinesSerializer());
        _ = registry.Use(ChipJsonContext.Default);
        _ = registry.Use<int>(new IntSerializer());

        frozen.Resolve<Chip>("chips").Format.ShouldBe("json-strict-v1", "the explicit registration after the freeze is not seen");
        Should.Throw<LuiConfigurationException>(() => frozen.Resolve<int>("numbers"), "a type registered after the freeze is not known to it");
        registry.Freeze().Resolve<int>("numbers").Format.ShouldBe("int-v1", "a registry that is frozen again is another copy");
    }

    [Fact]
    public void The_same_context_registered_twice_is_the_same_registration()
    {
        var frozen = new LuiPayloadRegistry().Use(PayloadJsonContext.Default).Use(PayloadJsonContext.Default).Freeze();

        frozen.Resolve<Chip>("chips").Format.ShouldBe("json-strict-v1");
    }

    [Fact]
    public void A_registration_that_is_no_registration_is_refused()
    {
        var registry = new LuiPayloadRegistry();

        Should.Throw<ArgumentNullException>(() => registry.Use((System.Text.Json.Serialization.JsonSerializerContext)null!)).ParamName.ShouldBe("context");
        Should.Throw<ArgumentNullException>(() => registry.Use<Chip>(null!)).ParamName.ShouldBe("serializer");
        var unnamed = Should.Throw<LuiConfigurationException>(() => registry.Use(new LinesSerializer("  ")));
        unnamed.Message.ShouldContain("Nordmann.LUI.Testing.Chip");
        unnamed.Message.ShouldContain("Format");
    }

    [Fact]
    public void The_registry_can_be_filled_from_several_threads()
    {
        var registry = new LuiPayloadRegistry();
        var accepted = 0;

        // every call registers the same type: exactly one wins and the others are refused — none is lost or torn
        Parallel.For(0, 64, i =>
        {
            try
            {
                _ = registry.Use(new NumberSerializer(i));
                _ = Interlocked.Increment(ref accepted);
            }
            catch (LuiConfigurationException)
            {
                // the refused ones
            }
        });

        accepted.ShouldBe(1);
        registry.Freeze().Resolve<long>("numbers").Format.ShouldStartWith("number-v");
    }

    private sealed class LinesSerializer(string format = "lines-v1") : ILuiPayloadSerializer<Chip>
    {
        public string Format { get; } = format;

        public byte[] Serialize(Chip payload) => System.Text.Encoding.UTF8.GetBytes($"{payload.Slug}\n{payload.Color}");

        public Chip Deserialize(ReadOnlySpan<byte> bytes)
        {
            var parts = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
            return new Chip(parts[0], parts[1].Length == 0 ? null : parts[1]);
        }
    }

    private sealed class IntSerializer : ILuiPayloadSerializer<int>
    {
        public string Format => "int-v1";

        public byte[] Serialize(int payload) => BitConverter.GetBytes(payload);

        public int Deserialize(ReadOnlySpan<byte> bytes) => BitConverter.ToInt32(bytes);
    }

    private sealed class NumberSerializer(int version) : ILuiPayloadSerializer<long>
    {
        public string Format => $"number-v{version}";

        public byte[] Serialize(long payload) => BitConverter.GetBytes(payload);

        public long Deserialize(ReadOnlySpan<byte> bytes) => BitConverter.ToInt64(bytes);
    }
}
