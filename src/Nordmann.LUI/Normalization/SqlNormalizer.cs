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

        return Apply(expression, Normalizer.Separators.Select(c => (c.ToString(), string.Empty)));
    }

    /// <summary>
    /// The German letters, composed and decomposed. The decomposed form is a
    /// letter plus the combining diaeresis — the C# side handles the same
    /// sequences, or the two spellings would be two different words.
    /// </summary>
    private static readonly (string From, string To)[] GermanReplacements =
    [
        ("a" + Normalizer.CombiningDiaeresis, "ae"),
        ("A" + Normalizer.CombiningDiaeresis, "ae"),
        ("o" + Normalizer.CombiningDiaeresis, "oe"),
        ("O" + Normalizer.CombiningDiaeresis, "oe"),
        ("u" + Normalizer.CombiningDiaeresis, "ue"),
        ("U" + Normalizer.CombiningDiaeresis, "ue"),
        ("ä", "ae"),
        ("Ä", "ae"),
        ("ö", "oe"),
        ("Ö", "oe"),
        ("ü", "ue"),
        ("Ü", "ue"),
        ("ß", "ss"),
        ("ẞ", "ss")
    ];

    private static string Apply(string expression, IEnumerable<(string From, string To)> replacements)
    {
        var result = expression;

        foreach (var (from, to) in replacements)
        {
            result = $"replace({result}, '{Escape(from)}', '{Escape(to)}')";
        }

        return result;
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
