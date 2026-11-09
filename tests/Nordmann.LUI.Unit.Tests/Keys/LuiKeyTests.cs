using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Keys;

/// <summary>
/// The key of an entry (ADR-0035): a number or a text, made without a cast and read back with one. The conversions into a
/// key lose nothing and never throw; the ones out of it are explicit, and they throw for the other kind.
/// </summary>
public sealed class LuiKeyTests
{
    [Fact]
    public void A_number_becomes_a_key_without_a_cast_and_comes_back_with_one()
    {
        LuiKey key = 42;

        key.Kind.ShouldBe(LuiKeyKind.Number);
        ((long)key).ShouldBe(42);
        key.ToInt64().ShouldBe(42);
        key.ToString().ShouldBe("42");
    }

    [Fact]
    public void A_text_becomes_a_key_without_a_cast_and_comes_back_with_one()
    {
        LuiKey key = "01JC0FFEE";

        key.Kind.ShouldBe(LuiKeyKind.Text);
        ((string)key).ShouldBe("01JC0FFEE");
        key.ToText().ShouldBe("01JC0FFEE");
        key.ToString().ShouldBe("01JC0FFEE");
    }

    [Fact]
    public void A_number_is_stored_in_invariant_digits()
    {
        LuiKey.FromInt64(-1_234_567).ToString().ShouldBe("-1234567");
        LuiKey.FromInt64(long.MaxValue).ToString().ShouldBe("9223372036854775807");
    }

    [Fact]
    public void A_number_and_a_text_that_read_the_same_are_different_keys()
    {
        LuiKey number = 1;
        LuiKey text = "1";

        (number == text).ShouldBeFalse();
        number.Equals(text).ShouldBeFalse();
        number.ShouldNotBe(text);
    }

    [Fact]
    public void Equal_keys_are_equal_and_hash_alike()
    {
        LuiKey a = "vegan";
        LuiKey b = "vegan";
        LuiKey n = 7;

        (a == b).ShouldBeTrue();
        (a != b).ShouldBeFalse();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        n.ShouldBe(LuiKey.FromInt64(7));
        n.GetHashCode().ShouldBe(LuiKey.FromInt64(7).GetHashCode());
        a.Equals((object)b).ShouldBeTrue();
        a.Equals((object)"vegan").ShouldBeFalse("a string is not a key");
    }

    [Fact]
    public void A_text_is_compared_ordinally()
    {
        ((LuiKey)"Vegan").ShouldNotBe((LuiKey)"vegan");
    }

    [Fact]
    public void The_conversion_out_of_a_key_throws_for_the_other_kind()
    {
        LuiKey number = 42;
        LuiKey text = "vegan";

        Should.Throw<InvalidCastException>(() => (string)number).Message.ShouldContain("42");
        Should.Throw<InvalidCastException>(() => (long)text).Message.ShouldContain("vegan");
    }

    [Fact]
    public void A_null_text_is_not_a_key()
    {
        Should.Throw<ArgumentNullException>(() => LuiKey.FromString(null!));
    }

    [Fact]
    public void The_default_key_is_the_number_zero()
    {
        default(LuiKey).Kind.ShouldBe(LuiKeyKind.Number);
        default(LuiKey).ShouldBe(LuiKey.FromInt64(0));
    }
}
