using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Keys;

/// <summary>
/// The key between the application and the file (ADR-0035): the file stores every key as text, the definition says
/// whether the texts are numbers or texts, and a key of the other kind is refused before it reaches the file.
/// </summary>
public sealed class KeyCodecTests
{
    [Fact]
    public void A_key_of_the_other_kind_is_refused_and_the_message_names_the_index_and_both_kinds()
    {
        var exception = Should.Throw<ArgumentException>(() => KeyCodec.ToText("vegan", LuiKeyKind.Number, "tags"));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldContain("'tags'");
        exception.Message.ShouldContain("numbers");
        exception.Message.ShouldContain("texts");
        Should.Throw<ArgumentException>(() => KeyCodec.ToText(1, LuiKeyKind.Text, "words"));
    }

    [Fact]
    public void A_key_goes_into_the_file_as_its_text_and_comes_back_as_its_kind()
    {
        KeyCodec.ToText(-42, LuiKeyKind.Number, "tags").ShouldBe("-42");
        KeyCodec.ToText("01JC0FFEE", LuiKeyKind.Text, "words").ShouldBe("01JC0FFEE");

        KeyCodec.FromText("-42", LuiKeyKind.Number).ShouldBe(LuiKey.FromInt64(-42));
        KeyCodec.FromText("42", LuiKeyKind.Text).ShouldBe(LuiKey.FromString("42"), "a text index keeps the text");
    }

    [Fact]
    public void The_kind_keeps_the_names_the_files_already_carry()
    {
        KeyCodec.KeyType(LuiKeyKind.Number).ShouldBe("long");
        KeyCodec.KeyType(LuiKeyKind.Text).ShouldBe("string");
        KeyCodec.KindOf("long").ShouldBe(LuiKeyKind.Number);
        KeyCodec.KindOf("string").ShouldBe(LuiKeyKind.Text);
    }

    [Fact]
    public void A_name_of_a_kind_that_is_neither_long_nor_string_is_refused()
    {
        Should.Throw<NotSupportedException>(() => KeyCodec.KindOf("int")).Message.ShouldContain("'int'");
    }
}
