using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Keys;

/// <summary>
/// The key of an entry (ADR-0035, ADR-0073): the type argument of the definition is the type of the key, and the file stores it as
/// text — a number in invariant digits, a string as it is, a GUID in the form <c>D</c>. The conversions at the border of the handles
/// lose nothing, and a stored text that is no key of the type is a <see cref="LuiKeyException"/>. (They replaced the struct <c>LuiKey</c>
/// of 0.3.0, which held a number or a text.)
/// </summary>
public sealed class KeyConversionTests
{
    [Fact]
    public void A_number_becomes_its_text_and_comes_back_as_a_number()
    {
        KeyConversion.ToText(42L).ShouldBe("42");
        KeyConversion.FromText<long>("42", "tags").ShouldBe(42L);
        KeyConversion.ToText(7).ShouldBe("7");
        KeyConversion.FromText<int>("7", "tags").ShouldBe(7);
    }

    [Fact]
    public void A_text_becomes_itself_and_comes_back_as_itself()
    {
        KeyConversion.ToText("01JC0FFEE").ShouldBe("01JC0FFEE");
        KeyConversion.FromText<string>("01JC0FFEE", "tags").ShouldBe("01JC0FFEE");
    }

    [Fact]
    public void A_number_is_stored_in_invariant_digits()
    {
        KeyConversion.ToText(-1_234_567L).ShouldBe("-1234567");
        KeyConversion.ToText(long.MaxValue).ShouldBe("9223372036854775807");
    }

    [Fact]
    public void A_number_and_a_text_that_read_the_same_are_told_apart_by_the_key_type_of_the_index()
    {
        // the file holds "1" for the number 1 and for the text "1": the key type of the definition says which it is
        KeyConversion.ToText(1L).ShouldBe(KeyConversion.ToText("1"));
        KeyConversion.FromText<long>("1", "tags").ShouldBe(1L);
        KeyConversion.FromText<string>("1", "tags").ShouldBe("1");
        KeyConversion.TypeOf<long>().ShouldNotBe(KeyConversion.TypeOf<string>());
    }

    [Fact]
    public void Equal_keys_have_equal_texts()
    {
        KeyConversion.ToText("vegan").ShouldBe(KeyConversion.ToText("vegan"));
        KeyConversion.ToText(7L).ShouldBe(KeyConversion.ToText(7L));
        KeyConversion.ToText(Guid.Parse("0F8FAD5B-D9CB-469F-A165-70867728950E")).ShouldBe(KeyConversion.ToText(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")));
    }

    [Fact]
    public void A_text_is_compared_ordinally()
    {
        string.Equals(KeyConversion.ToText("Vegan"), KeyConversion.ToText("vegan"), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_stored_text_that_is_no_key_of_the_type_is_a_key_exception_naming_it()
    {
        Should.Throw<LuiKeyException>(() => KeyConversion.FromText<long>("vegan", "tags")).Message.ShouldContain("vegan");
        Should.Throw<LuiKeyException>(() => KeyConversion.FromText<Guid>("42", "ids")).Message.ShouldContain("42");
        Should.Throw<LuiKeyException>(() => KeyConversion.FromText<int>("3000000000", "counts")).Message.ShouldContain("3000000000");
    }

    [Fact]
    public void The_type_of_a_key_is_the_type_argument()
    {
        KeyConversion.TypeOf<long>().ShouldBe(LuiKeyType.Long);
        KeyConversion.TypeOf<int>().ShouldBe(LuiKeyType.Int);
        KeyConversion.TypeOf<string>().ShouldBe(LuiKeyType.String);
        KeyConversion.TypeOf<Guid>().ShouldBe(LuiKeyType.Guid);
        Should.Throw<NotSupportedException>(() => KeyConversion.TypeOf<decimal>());
    }

    [Fact]
    public void The_default_number_is_the_key_zero()
    {
        KeyConversion.ToText(default(long)).ShouldBe("0");
        KeyConversion.FromText<long>("0", "tags").ShouldBe(0L);
    }
}
