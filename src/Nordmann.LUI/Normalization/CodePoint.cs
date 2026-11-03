namespace Nordmann.LUI.Normalization;

/// <summary>
/// Lengths in code points, not UTF-16 units: the unit of SQLite's <c>substr</c> and of the trigram — a
/// surrogate pair is one.
/// </summary>
internal static class CodePoint
{
    /// <summary>The number of code points of a text.</summary>
    internal static int Count(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (!char.IsLowSurrogate(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The first <paramref name="count"/> code points of a text, or the whole text.</summary>
    internal static string First(string text, int count)
    {
        var taken = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLowSurrogate(text[i]) && taken++ == count)
            {
                return text[..i];
            }
        }

        return text;
    }
}
