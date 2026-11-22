using System.Reflection;
using System.Reflection.Emit;
using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Payload;

/// <summary>
/// The name of a payload type as the index stores it (ADR-0072): the value of <c>[LuiPayloadName]</c>, or a name that LUI
/// computes without the assembly, its version or its public key — so that a rebuild with another assembly version is not a
/// changed definition.
/// </summary>
public sealed class PayloadTypeNameTests
{
    public static TheoryData<Type, string> Computed => new()
    {
        { typeof(Chip), "Nordmann.LUI.Testing.Chip" },
        { typeof(string), "System.String" },
        { typeof(int), "System.Int32" },
        { typeof(Nested), "Nordmann.LUI.Unit.Tests.Payload.PayloadTypeNameTests+Nested" },
        { typeof(Nested.Deeper), "Nordmann.LUI.Unit.Tests.Payload.PayloadTypeNameTests+Nested+Deeper" },
        { typeof(List<Chip>), "System.Collections.Generic.List<Nordmann.LUI.Testing.Chip>" },
        { typeof(Dictionary<string, int>), "System.Collections.Generic.Dictionary<System.String,System.Int32>" },
        { typeof(List<List<Chip>>), "System.Collections.Generic.List<System.Collections.Generic.List<Nordmann.LUI.Testing.Chip>>" },
        { typeof(int?), "System.Nullable<System.Int32>" },
        { typeof(Envelope<Chip>), "Nordmann.LUI.Unit.Tests.Payload.PayloadTypeNameTests+Envelope<Nordmann.LUI.Testing.Chip>" },
        { typeof(Envelope<Chip>.Inner<int>), "Nordmann.LUI.Unit.Tests.Payload.PayloadTypeNameTests+Envelope<Nordmann.LUI.Testing.Chip>+Inner<System.Int32>" },
        { typeof(Chip[]), "Nordmann.LUI.Testing.Chip[]" },
        { typeof(Chip[][]), "Nordmann.LUI.Testing.Chip[][]" },
        { typeof(int[,]), "System.Int32[,]" },
        { typeof(List<Chip>[]), "System.Collections.Generic.List<Nordmann.LUI.Testing.Chip>[]" },
        { typeof(Nested[]), "Nordmann.LUI.Unit.Tests.Payload.PayloadTypeNameTests+Nested[]" },
    };

    [Theory]
    [MemberData(nameof(Computed))]
    public void Computed_names_are_stable(Type type, string expected)
    {
        var name = PayloadTypeName.Of(type);

        name.ShouldBe(expected);
        name.ShouldNotContain("Version=");
        name.ShouldNotContain("Culture=");
        name.ShouldNotContain("PublicKeyToken");
        name.ShouldNotContain("System.Private.CoreLib");
        name.ShouldNotContain("Nordmann.LUI.Testing,");
        name.ShouldNotContain("`", Case.Sensitive, "no arity suffix");
        PayloadTypeName.Of(type).ShouldBe(name, "the same type has the same name every time");
    }

    [Fact]
    public void The_attribute_name_survives_a_rename()
    {
        // the class was renamed and moved to another namespace: the name that is stored is the one the attribute gives
        PayloadTypeName.Of(typeof(OldRecipe)).ShouldBe("recipe");
        PayloadTypeName.Of(typeof(One.RenamedRecipe)).ShouldBe("recipe");
        PayloadTypeName.Of(typeof(One.RenamedRecipe)).ShouldBe(PayloadTypeName.Of(typeof(OldRecipe)));
        PayloadTypeName.Of(typeof(Recipe)).ShouldBe("Nordmann.LUI.Unit.Tests.Support.Recipe", "a type without the attribute has its computed name");
    }

    [Fact]
    public void The_attribute_name_is_the_name_of_the_type_inside_generic_types_and_arrays()
    {
        PayloadTypeName.Of(typeof(OldRecipe[])).ShouldBe("recipe[]");
        PayloadTypeName.Of(typeof(List<OldRecipe>)).ShouldBe("System.Collections.Generic.List<recipe>");
        PayloadTypeName.Of(typeof(Wrapped<Chip>)).ShouldBe("wrapped<Nordmann.LUI.Testing.Chip>", "the arguments stay: two closed types are not one name");
        PayloadTypeName.Of(typeof(Wrapped<OldRecipe>)).ShouldBe("wrapped<recipe>");
    }

    [Fact]
    public void An_attribute_without_a_name_is_refused_and_names_the_type()
    {
        var exception = Should.Throw<LuiConfigurationException>(() => PayloadTypeName.Of(typeof(Unnamed)));

        exception.Message.ShouldContain("Unnamed");
        exception.Message.ShouldContain(nameof(LuiPayloadNameAttribute));
    }

    [Fact]
    public void A_name_does_not_change_with_the_version_of_the_assembly()
    {
        // the same type — the same namespace, the same name, nested and generic — in two builds that differ in the version
        var first = Build(new Version(1, 0, 0, 0));
        var second = Build(new Version(2, 5, 0, 0));

        first.Payload.Assembly.GetName().Version.ShouldNotBe(second.Payload.Assembly.GetName().Version);
        first.Payload.AssemblyQualifiedName.ShouldNotBe(second.Payload.AssemblyQualifiedName, "the assembly-qualified names do differ");

        PayloadTypeName.Of(first.Payload).ShouldBe("Dyn.Recipes.Payload");
        PayloadTypeName.Of(second.Payload).ShouldBe(PayloadTypeName.Of(first.Payload));
        PayloadTypeName.Of(first.Inner).ShouldBe("Dyn.Recipes.Payload+Inner");
        PayloadTypeName.Of(second.Inner).ShouldBe(PayloadTypeName.Of(first.Inner));
        PayloadTypeName.Of(typeof(List<>).MakeGenericType(first.Payload)).ShouldBe("System.Collections.Generic.List<Dyn.Recipes.Payload>");
        PayloadTypeName.Of(typeof(List<>).MakeGenericType(second.Payload)).ShouldBe(PayloadTypeName.Of(typeof(List<>).MakeGenericType(first.Payload)));
        PayloadTypeName.Of(first.Payload.MakeArrayType()).ShouldBe("Dyn.Recipes.Payload[]");
        PayloadTypeName.Of(second.Payload.MakeArrayType()).ShouldBe(PayloadTypeName.Of(first.Payload.MakeArrayType()));
    }

    private static (Type Payload, Type Inner) Build(Version version)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Dyn.Payloads.{version}") { Version = version }, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("main");
        var payload = module.DefineType("Dyn.Recipes.Payload", TypeAttributes.Public | TypeAttributes.Class);
        var inner = payload.DefineNestedType("Inner", TypeAttributes.NestedPublic | TypeAttributes.Class);
        var innerType = inner.CreateType();
        return (payload.CreateType(), innerType);
    }

    public sealed class Nested
    {
        public sealed class Deeper;
    }

    public sealed class Envelope<T>
    {
        public sealed class Inner<TOther>;
    }

    [LuiPayloadName("recipe")]
    public sealed class OldRecipe;

    [LuiPayloadName("wrapped")]
    public sealed class Wrapped<T>;

    [LuiPayloadName("  ")]
    public sealed class Unnamed;

    public static class One
    {
        [LuiPayloadName("recipe")]
        public sealed class RenamedRecipe;
    }
}
