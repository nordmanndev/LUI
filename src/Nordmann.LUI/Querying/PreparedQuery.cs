using System.Text;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Querying;

/// <summary>
/// What the user typed, in the forms the query path needs (LUI-Spezifikation §8).
/// </summary>
internal sealed class PreparedQuery
{
    /// <summary>The shortest input the trigram tokenizer can search for.</summary>
    internal const int MinimumTrigramLength = 3;

    private PreparedQuery(string search, string compact)
    {
        Search = search;
        Compact = compact;
        PrefixKey = search.Replace("#", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The search form: what is compared with the index.</summary>
    internal string Search { get; }

    /// <summary>The compact form: for the word-start rule.</summary>
    internal string Compact { get; }

    /// <summary>The search form without <c>#</c> — the key of the prefix columns.</summary>
    internal string PrefixKey { get; }

    /// <summary>Whether the input is too short for a trigram: the prefix path answers it (§8.1).</summary>
    internal bool IsPrefixPath => Search.Length < MinimumTrigramLength;

    /// <summary>Nothing to search for.</summary>
    internal bool IsEmpty => IsPrefixPath && PrefixKey.Length == 0;

    internal static PreparedQuery Create(string input, NormalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);

        // A NUL cannot be part of an FTS5 string; it is not part of any word either.
        var text = input.Contains('\0', StringComparison.Ordinal)
            ? input.Replace("\0", string.Empty, StringComparison.Ordinal)
            : input;

        text = text.Trim();

        return new PreparedQuery(
            Normalizer.ToSearchForm(text, options),
            Normalizer.ToCompactForm(text, options));
    }

    /// <summary>
    /// The FTS5 expression for a single-word index: <c>s_norm : "…"</c>. The
    /// term is quoted, quotation marks doubled — the input is never copied
    /// into the expression raw (§8.2).
    /// </summary>
    internal string SingleWordExpression()
    {
        var builder = new StringBuilder("s_norm : \"", Search.Length + 12);
        builder.Append(Search.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
        return builder.ToString();
    }
}
