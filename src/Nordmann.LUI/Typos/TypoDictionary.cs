using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Typos;

/// <summary>
/// The typo dictionary of one index (LUI-Spezifikation §10): SymSpell over the <b>word beginnings</b> of the
/// normalised texts, 3 … 10 letters each, with the weights as frequency, one edit.
/// <para>
/// Why beginnings: a user types the start of a word. SymSpell over whole words corrects a word that is not
/// finished hardly at all (11 %); over the beginnings it reaches 40 % — most of what is possible without a
/// typo (Anhang A4).
/// </para>
/// <para>
/// It lives in memory, is built <b>lazily</b> on the first correction (the 676,000 word beginnings of a
/// large index cost about 180 MB and a few seconds, which the application should not pay at start), and
/// grows with <see cref="Add"/>. A word that is removed stays (SymSpell cannot forget): a correction towards
/// it finds nothing. <see cref="Rebuild"/> starts anew. SymSpell is not made for reading while writing, so
/// the dictionary has a lock: lookups share it, additions and the build have it alone (§15).
/// </para>
/// </summary>
#pragma warning disable CA1001 // the lock allocates a kernel event only when a thread has to wait; it is reclaimed by the GC
internal sealed class TypoDictionary
#pragma warning restore CA1001
{
    /// <summary>A word of fewer letters is not looked up (§10.3).</summary>
    internal const int MinimumWordLength = 3;

    /// <summary>The longest beginning in the dictionary (§10.1).</summary>
    internal const int LongestBeginning = 10;

    /// <summary>The most corrections that are searched (§10.3).</summary>
    internal const int MaxVariants = 3;

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly NormalizationOptions _normalization;
    private readonly Func<IEnumerable<(string Text, int Weight)>> _source;
    private SymSpell? _symSpell;

    internal TypoDictionary(NormalizationOptions normalization, Func<IEnumerable<(string Text, int Weight)>> source)
    {
        _normalization = normalization;
        _source = source;
    }

    /// <summary>Whether the dictionary has been built.</summary>
    internal bool IsBuilt
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _symSpell is not null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>Builds the dictionary from the content table, once; later calls return at once.</summary>
    internal void EnsureBuilt(CancellationToken cancellationToken)
    {
        if (IsBuilt)
        {
            return;
        }

        _lock.EnterWriteLock();
        try
        {
            _symSpell ??= Build(cancellationToken);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Builds the dictionary anew: the beginnings of words that were removed are gone.</summary>
    internal void Rebuild()
    {
        // the build reads the content table; the swap is the only moment readers wait
        var fresh = Build(CancellationToken.None);

        _lock.EnterWriteLock();
        try
        {
            _symSpell = fresh;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds the word beginnings of a text that are not in the dictionary yet. Nothing happens while the
    /// dictionary is not built: the build reads the table and finds the text there.
    /// </summary>
    internal void Add(string text, int weight)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_symSpell is { } symSpell)
            {
                AddBeginnings(symSpell, text, weight, onlyNew: true);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// The corrected inputs, best first, at most <see cref="MaxVariants"/>, none equal to the input
    /// (§10.3): every word of three or more letters is looked up (closest, one edit); the k-th variant takes
    /// the k-th suggestion of every word that has one. A word longer than ten letters is looked up by
    /// its first ten and keeps the rest. Empty when the dictionary is not built or nothing is near.
    /// </summary>
    /// <param name="search">The input in search form.</param>
    internal IReadOnlyList<string> Corrections(string search)
    {
        var words = Words(search);
        if (words.Count == 0)
        {
            return [];
        }

        _lock.EnterReadLock();
        try
        {
            if (_symSpell is not { } symSpell)
            {
                return [];
            }

            var suggestions = new List<string[]?>(words.Count);
            foreach (var word in words)
            {
                suggestions.Add(Suggest(symSpell, search.AsSpan(word.Start, word.Length)));
            }

            return Variants(search, words, suggestions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private static string[]? Suggest(SymSpell symSpell, ReadOnlySpan<char> word)
    {
        var text = word.ToString();
        var head = CodePointPrefix(text, LongestBeginning);
        var tail = text[head.Length..];

        var found = symSpell.Lookup(head, SymSpell.Verbosity.Closest, 1);

        // the word is a beginning of a word of the index — nothing to correct; or nothing is near
        if (found.Count == 0 || found[0].distance == 0)
        {
            return null;
        }

        // The beginnings of one word share its frequency, and a shorter beginning is never rarer than a longer one:
        // by frequency alone `apfe` would beat `apfel` for `apfle`. So among the nearest the terms of the length that
        // was typed come first (two letters swapped, one wrong), then the most frequent.
        return [.. found
            .OrderBy(item => item.distance)
            .ThenBy(item => item.term.Length == head.Length ? 0 : 1)
            .ThenByDescending(item => item.count)
            .ThenBy(item => item.term, StringComparer.Ordinal)
            .Take(MaxVariants)
            .Select(item => item.term + tail)];
    }

    private static List<string> Variants(string search, List<(int Start, int Length)> words, List<string[]?> suggestions)
    {
        var variants = new List<string>(MaxVariants);

        for (var k = 0; k < MaxVariants; k++)
        {
            var builder = new System.Text.StringBuilder(search.Length + 4);
            var position = 0;

            for (var i = 0; i < words.Count; i++)
            {
                builder.Append(search, position, words[i].Start - position);
                var options = suggestions[i];
                builder.Append(options is null
                    ? search.AsSpan(words[i].Start, words[i].Length)
                    : options[Math.Min(k, options.Length - 1)].AsSpan());
                position = words[i].Start + words[i].Length;
            }

            builder.Append(search, position, search.Length - position);

            var variant = builder.ToString();
            if (!string.Equals(variant, search, StringComparison.Ordinal) && !variants.Contains(variant, StringComparer.Ordinal))
            {
                variants.Add(variant);
            }
        }

        return variants;
    }

    private SymSpell Build(CancellationToken cancellationToken)
    {
        // one edit, the default prefix length; a count of at least 1 keeps every beginning
        var symSpell = new SymSpell(initialCapacity: 1 << 16, maxDictionaryEditDistance: 1, prefixLength: 7, countThreshold: 1);

        var rows = 0;
        foreach (var (text, weight) in _source())
        {
            if ((rows++ & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            AddBeginnings(symSpell, text, weight, onlyNew: false);
        }

        return symSpell;
    }

    private void AddBeginnings(SymSpell symSpell, string text, int weight, bool onlyNew)
    {
        var search = Normalizer.ToSearchForm(text, _normalization);

        foreach (var (start, length) in Words(search))
        {
            var word = search.Substring(start, length);
            var codePoints = 0;

            for (var i = 0; i < word.Length; i++)
            {
                if (char.IsLowSurrogate(word[i]))
                {
                    continue;
                }

                if (++codePoints > LongestBeginning)
                {
                    break;
                }

                if (codePoints < MinimumWordLength)
                {
                    continue;
                }

                // the beginning ends with this code point, a surrogate pair included
                var end = i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]) ? i + 2 : i + 1;
                var beginning = word[..end];

                if (onlyNew && symSpell.Lookup(beginning, SymSpell.Verbosity.Top, 0).Count > 0)
                {
                    continue;
                }

                // weight + 1: a word with the weight 0 is still a word (SymSpell drops a count of 0)
                symSpell.CreateDictionaryEntry(beginning, weight + 1L, null);
            }
        }
    }

    /// <summary>The words of a search form that are long enough to be looked up, with their positions.</summary>
    private static List<(int Start, int Length)> Words(string search)
    {
        var words = new List<(int, int)>();
        var start = -1;

        for (var i = 0; i <= search.Length; i++)
        {
            var separator = i == search.Length || Normalizer.IsSeparator(search[i]);
            if (!separator && start < 0)
            {
                start = i;
            }
            else if (separator && start >= 0)
            {
                if (CodePointCount(search.AsSpan(start, i - start)) >= MinimumWordLength)
                {
                    words.Add((start, i - start));
                }

                start = -1;
            }
        }

        return words;
    }

    private static int CodePointCount(ReadOnlySpan<char> text)
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

    private static string CodePointPrefix(string text, int codePoints)
    {
        var taken = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLowSurrogate(text[i]) && taken++ == codePoints)
            {
                return text[..i];
            }
        }

        return text;
    }
}
