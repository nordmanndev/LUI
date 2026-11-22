using System.Reflection;
using System.Text;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The name of a payload type as an embedded index stores it (ADR-0072): the value of <see cref="LuiPayloadNameAttribute"/>, or a
/// name that LUI computes — <b>without the assembly, its version or its public key</b>, so that a rebuild with another assembly
/// version is not a changed definition. A non-generic type is its namespace-qualified name (nested types as <c>Outer+Inner</c>), a
/// generic type its namespace-qualified name without the arity and with the stable names of its type arguments
/// (<c>System.Collections.Generic.List&lt;MyApp.Tag&gt;</c>), an array the name of its element type with <c>[]</c> (<c>[,]</c> for two
/// dimensions).
/// </summary>
internal static class PayloadTypeName
{
    /// <summary>The stable name of a payload type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name; the same for the same type in every build.</returns>
    /// <exception cref="LuiConfigurationException">The attribute of the type gives no name.</exception>
    /// <exception cref="ArgumentException">The type is an open generic type or a type parameter — no payload has one.</exception>
    internal static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.ContainsGenericParameters)
        {
            throw new ArgumentException($"The type '{type}' is an open generic type: a payload type has all its type arguments.", nameof(type));
        }

        var builder = new StringBuilder();
        Append(builder, type);
        return builder.ToString();
    }

    /// <summary>
    /// Why a type cannot be the payload type of an index (ADR-0072): <see cref="object"/>, an interface and an abstract class stand for
    /// <b>many</b> types, so an index of one would hold the payloads of any type — the untyped payload that has no place. A source-generated
    /// context answers for <see cref="object"/> whatever it lists, so the registry cannot be left to refuse it.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The reason, or <see langword="null"/> when the type is one concrete type.</returns>
    internal static string? NotASingleType(Type type)
    {
        var kind = type == typeof(object) ? "object" : type.IsInterface ? "an interface" : type.IsAbstract && !type.IsArray ? "an abstract class" : null;
        return kind is null
            ? null
            : $"the payload type '{type.FullName}' is {kind}, not one single type: an index stores the payloads of exactly one concrete type, "
              + "and a type that stands for many is a catch-all serializer under another name. Declare the concrete type (a sealed class or record, "
              + "or a sealed envelope that holds the variants) and register it with LuiPayloadRegistry.Use(context) or Use<TConcrete>(serializer).";
    }

    private static void Append(StringBuilder builder, Type type)
    {
        if (type.IsArray)
        {
            Append(builder, type.GetElementType()!);
            var rank = type.GetArrayRank();
            _ = builder.Append('[').Append(',', rank - 1).Append(']');
            return;
        }

        var arguments = type.IsGenericType ? type.GetGenericArguments() : [];
        var named = NameOf(type);
        if (named is not null)
        {
            // the attribute names the type: the arguments of a closed generic type stay, two closed types are not one name
            _ = builder.Append(named);
            AppendArguments(builder, arguments);
            return;
        }

        AppendSegment(builder, type, arguments);
    }

    /// <summary>One type of the chain of the declaring types: <c>Namespace.Outer&lt;A&gt;+Inner&lt;B&gt;</c>.</summary>
    private static void AppendSegment(StringBuilder builder, Type type, Type[] arguments)
    {
        var consumed = 0;
        if (type.DeclaringType is { } declaring)
        {
            consumed = declaring.IsGenericType ? declaring.GetGenericArguments().Length : 0;
            AppendSegment(builder, declaring, arguments[..consumed]);
            _ = builder.Append('+');
        }
        else if (!string.IsNullOrEmpty(type.Namespace))
        {
            _ = builder.Append(type.Namespace).Append('.');
        }

        var name = type.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        _ = builder.Append(arity < 0 ? name : name[..arity]);
        AppendArguments(builder, arguments[consumed..]);
    }

    private static void AppendArguments(StringBuilder builder, Type[] arguments)
    {
        if (arguments.Length == 0)
        {
            return;
        }

        _ = builder.Append('<');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(',');
            }

            Append(builder, arguments[i]);
        }

        _ = builder.Append('>');
    }

    /// <summary>The name the attribute gives, or <see langword="null"/> when the type has none.</summary>
    private static string? NameOf(Type type)
    {
        // the attribute of a closed generic type is the one of its definition
        var attribute = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).GetCustomAttribute<LuiPayloadNameAttribute>(inherit: false);
        if (attribute is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(attribute.Name)
            ? throw new LuiConfigurationException(
                $"The payload type '{type.FullName}' has a {nameof(LuiPayloadNameAttribute)} without a name: give the attribute the name that is "
                + "stored for the type, or remove it.")
            : attribute.Name;
    }
}
