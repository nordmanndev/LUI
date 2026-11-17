namespace Nordmann.LUI.Schema;

/// <summary>
/// Checks for the values of the options and entries, made in their <c>init</c> accessors: the error comes
/// where the value is set, and names the property that was set — in the message and as its
/// <see cref="ArgumentException.ParamName"/> (review of pull request 12).
/// <para>
/// The values of the connections are checked when the catalog is opened (ADR-0066) — the error names the property
/// there, too, and comes before a connection is opened.
/// </para>
/// </summary>
internal static class Setting
{
    internal static int InRange(int value, int minimum, int maximum, string property) =>
        value >= minimum && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(property, value, $"{property} is {minimum:N0} … {maximum:N0}.");

    internal static TimeSpan InRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string property) =>
        value >= minimum && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(property, value, $"{property} is {minimum} … {maximum}.");

    internal static int AtLeast(int value, int minimum, string property) =>
        value >= minimum
            ? value
            : throw new ArgumentOutOfRangeException(property, value, $"{property} is at least {minimum:N0}.");

    internal static TimeSpan NotNegative(TimeSpan value, string property) =>
        value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(property, value, $"{property} is not negative.");

    internal static T NotNull<T>(T? value, string property)
        where T : class =>
        value ?? throw new ArgumentNullException(property, $"{property} is not null.");

    internal static string NotEmpty(string value, string property) => value switch
    {
        null => throw new ArgumentNullException(property, $"{property} is not null."),
        "" => throw new ArgumentException($"{property} is not empty.", property),
        _ => value,
    };

    internal static TEnum Defined<TEnum>(TEnum value, string property)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(property, value, $"{property}: '{value}' is not one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
}
