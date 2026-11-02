using System.Text;

namespace Nordmann.LUI.Normalization;

/// <summary>
/// The SQL side of the normalisation rule: the expressions the generated
/// columns of an index use.
/// <para>
/// The SQL side can only do what SQLite can do without extensions: replace
/// the German letters and strip the separators. The case fold and the removal
/// of the diacritics are left to the tokenizer — it applies them to the
/// indexed text and to the query alike, so both sides always agree.
/// </para>
/// </summary>
internal static class SqlNormalizer
{
    /// <summary>
    /// The expression that applies the German transliteration to
    /// <paramref name="expression"/>.
    /// </summary>
    internal static string Transliteration(string expression, bool germanTransliteration = true)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (!germanTransliteration)
        {
            return expression;
        }

        return Apply(expression, GermanReplacements);
    }

    /// <summary>
    /// The expression that removes the separators from
    /// <paramref name="expression"/> — the compact form.
    /// </summary>
    internal static string Compact(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return Apply(expression, Normalizer.Separators.Select(c => (c, string.Empty)));
    }

    private static readonly (char From, string To)[] GermanReplacements =
    [
        ('ä', "ae"),
        ('Ä', "ae"),
        ('ö', "oe"),
        ('Ö', "oe"),
        ('ü', "ue"),
        ('Ü', "ue"),
        ('ß', "ss"),
        ('ẞ', "ss")
    ];

    private static string Apply(string expression, IEnumerable<(char From, string To)> replacements)
    {
        var result = expression;

        foreach (var (from, to) in replacements)
        {
            result = $"replace({result}, '{Escape(from)}', '{Escape(to)}')";
        }

        return result;
    }

    private static string Escape(char value) => value.ToString().Replace("'", "''", StringComparison.Ordinal);

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
