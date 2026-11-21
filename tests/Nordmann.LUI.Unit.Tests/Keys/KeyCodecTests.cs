using System.Globalization;
using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Keys;

/// <summary>
/// The key between the application and the file (ADR-0035, ADR-0073): the file stores every key as text, the definition names
/// the exact type of the keys — <c>long</c>, <c>int</c>, <c>string</c> or <c>guid</c> —, a key of the other kind is refused
/// before it reaches the file, and a stored key that does not convert fails the read with a <see cref="LuiKeyException"/>.
/// </summary>
public sealed class KeyCodecTests
{
    [Fact]
    public void A_key_of_the_other_kind_is_refused_and_the_message_names_the_index_and_both_kinds()
    {
        var exception = Should.Throw<ArgumentException>(() => KeyCodec.ToText("vegan", LuiKeyType.Long, "tags"));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldContain("'tags'");
        exception.Message.ShouldContain("numbers");
        exception.Message.ShouldContain("texts");
        Should.Throw<ArgumentException>(() => KeyCodec.ToText(1, LuiKeyType.String, "words"));
        Should.Throw<ArgumentException>(() => KeyCodec.ToText("vegan", LuiKeyType.Int, "tags"));
        Should.Throw<ArgumentException>(() => KeyCodec.ToText(1, LuiKeyType.Guid, "ids"));
    }

    [Fact]
    public void A_key_goes_into_the_file_as_its_text_and_comes_back_as_its_kind()
    {
        KeyCodec.ToText(-42, LuiKeyType.Long, "tags").ShouldBe("-42");
        KeyCodec.ToText("01JC0FFEE", LuiKeyType.String, "words").ShouldBe("01JC0FFEE");

        KeyCodec.FromText("-42", LuiKeyType.Long, "tags").ShouldBe(LuiKey.FromInt64(-42));
        KeyCodec.FromText("42", LuiKeyType.String, "words").ShouldBe(LuiKey.FromString("42"), "a text index keeps the text");
    }

    [Fact]
    public void An_int_key_round_trips()
    {
        foreach (var number in new[] { int.MinValue, -1, 0, 1, 42, int.MaxValue })
        {
            var text = KeyCodec.ToText(number, LuiKeyType.Int, "counts");

            text.ShouldBe(number.ToString(CultureInfo.InvariantCulture));
            KeyCodec.FromText(text, LuiKeyType.Int, "counts").ShouldBe(LuiKey.FromInt64(number));
        }
    }

    [Fact]
    public void An_int_key_outside_the_int_range_is_refused_on_the_write()
    {
        var exception = Should.Throw<ArgumentException>(() => KeyCodec.ToText(2_147_483_648, LuiKeyType.Int, "counts"));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldContain("'counts'");
        exception.Message.ShouldContain("2147483648");
        Should.Throw<ArgumentException>(() => KeyCodec.ToText(-2_147_483_649, LuiKeyType.Int, "counts"));
    }

    [Fact]
    public void A_guid_key_is_stored_canonically_in_lower_case_D_form()
    {
        var guid = new Guid("0F8FAD5B-D9CB-469F-A165-70867728950E");

        KeyCodec.ToText(guid.ToString("D").ToUpperInvariant(), LuiKeyType.Guid, "ids").ShouldBe("0f8fad5b-d9cb-469f-a165-70867728950e");
        KeyCodec.ToText(guid.ToString("D"), LuiKeyType.Guid, "ids").ShouldBe("0f8fad5b-d9cb-469f-a165-70867728950e");
        KeyCodec.FromText("0f8fad5b-d9cb-469f-a165-70867728950e", LuiKeyType.Guid, "ids")
            .ShouldBe(LuiKey.FromString("0f8fad5b-d9cb-469f-a165-70867728950e"));
        KeyCodec.FromText("0F8FAD5B-D9CB-469F-A165-70867728950E", LuiKeyType.Guid, "ids")
            .ShouldBe(LuiKey.FromString("0f8fad5b-d9cb-469f-a165-70867728950e"), "a stored key that is a GUID in another case reads as the canonical one");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("{0f8fad5b-d9cb-469f-a165-70867728950e}")]
    [InlineData("(0f8fad5b-d9cb-469f-a165-70867728950e)")]
    public void A_text_that_is_not_a_guid_in_D_form_is_refused_on_a_guid_index(string text)
    {
        var exception = Should.Throw<ArgumentException>(() => KeyCodec.ToText(LuiKey.FromString(text), LuiKeyType.Guid, "ids"));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldContain("'ids'");
    }

    [Theory]
    [InlineData(LuiKeyType.Long, "abc")]
    [InlineData(LuiKeyType.Long, "")]
    [InlineData(LuiKeyType.Long, "9223372036854775808")]
    [InlineData(LuiKeyType.Int, "abc")]
    [InlineData(LuiKeyType.Int, "2147483648")]
    [InlineData(LuiKeyType.Int, "-2147483649")]
    [InlineData(LuiKeyType.Int, "1.5")]
    [InlineData(LuiKeyType.Guid, "not-a-guid")]
    [InlineData(LuiKeyType.Guid, "")]
    [InlineData(LuiKeyType.Guid, "0f8fad5bd9cb469fa16570867728950e")]
    public void A_damaged_stored_key_throws_a_key_exception_naming_index_and_key(LuiKeyType keyType, string stored)
    {
        var exception = Should.Throw<LuiKeyException>(() => KeyCodec.FromText(stored, keyType, "tags"));

        exception.IndexName.ShouldBe("tags");
        exception.StoredKey.ShouldBe(stored);
        exception.KeyType.ShouldBe(keyType);
        exception.Message.ShouldContain("'tags'");
        exception.Message.ShouldContain($"'{stored}'");
        exception.ShouldBeAssignableTo<LuiException>();
    }

    [Fact]
    public void A_stored_text_is_always_a_valid_key_of_a_string_index()
    {
        KeyCodec.FromText(string.Empty, LuiKeyType.String, "words").ShouldBe(LuiKey.FromString(string.Empty));
        KeyCodec.FromText("not-a-guid", LuiKeyType.String, "words").ShouldBe(LuiKey.FromString("not-a-guid"));
    }

    [Fact]
    public void The_key_type_keeps_the_names_the_files_already_carry_and_names_the_new_ones_exactly()
    {
        KeyCodec.Name(LuiKeyType.Long).ShouldBe("long");
        KeyCodec.Name(LuiKeyType.String).ShouldBe("string");
        KeyCodec.Name(LuiKeyType.Int).ShouldBe("int");
        KeyCodec.Name(LuiKeyType.Guid).ShouldBe("guid");

        foreach (var keyType in Enum.GetValues<LuiKeyType>())
        {
            KeyCodec.IsKnown(KeyCodec.Name(keyType)).ShouldBeTrue();
            KeyCodec.TypeOf(KeyCodec.Name(keyType)).ShouldBe(keyType);
        }

        KeyCodec.KindOf("long").ShouldBe(LuiKeyKind.Number);
        KeyCodec.KindOf("int").ShouldBe(LuiKeyKind.Number);
        KeyCodec.KindOf("string").ShouldBe(LuiKeyKind.Text);
        KeyCodec.KindOf("guid").ShouldBe(LuiKeyKind.Text);
    }

    [Fact]
    public void A_name_of_a_key_type_that_is_none_of_the_four_is_refused()
    {
        KeyCodec.IsKnown("decimal").ShouldBeFalse();
        KeyCodec.IsKnown("Guid").ShouldBeFalse("the stored name is lower case");
        Should.Throw<NotSupportedException>(() => KeyCodec.KindOf("decimal")).Message.ShouldContain("'decimal'");
        Should.Throw<NotSupportedException>(() => KeyCodec.TypeOf("decimal")).Message.ShouldContain("'decimal'");
    }
}
