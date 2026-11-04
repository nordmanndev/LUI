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
/// It lives in memory, is built <b>lazily</b> on the first correction (the word beginnings of 685,000 words
/// cost about 140 MB and a few seconds, which the application should not pay at start), and grows with
/// <see cref="Add"/>. A word that is removed stays (SymSpell cannot forget): a correction towards it finds
/// nothing. <see cref="Rebuild"/> starts anew. SymSpell is not made for reading while writing, so the
/// dictionary has a lock: lookups share it, an addition has it alone (§15) — the build does not, it works
/// on a dictionary of its own and swaps it in, so a write never waits for it.
/// </para>
/// <para>
/// A write inside a transaction of the application is learned when it is <b>written</b>, not when it is
/// committed — the library does not own the transaction and does not learn of its end. A rollback therefore
/// leaves the beginnings of its texts behind, and an input that is exactly such a beginning is not corrected:
/// to the dictionary it is a word of the index (review of pull request 12). <see cref="Rebuild"/> brings the
/// dictionary back in step with the table.
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

    // The lock guards the structure of the dictionary that readers see: lookups share it, an addition has it
    // alone. The build does NOT hold it — a build takes seconds, and a write must not wait that long
    // (review of pull request 10, M1). It builds a dictionary of its own and swaps it in.
    private readonly ReaderWriterLockSlim _lock = new();

    // One build at a time.
    private readonly Lock _buildGate = new();

    // Guards _pending: the writes that arrive while a dictionary is being built are queued here and replayed
    // on the new dictionary before it is swapped in — so neither the first build nor a rebuild loses a word.
    private readonly Lock _pendingGate = new();

    private readonly NormalizationOptions _normalization;
    private readonly Func<IEnumerable<(string Text, int Weight)>> _source;
    private SymSpell? _symSpell;
    private Queue<(string Text, int Weight)>? _pending;

    // the builds that run (0 or 1: one at a time behind the build gate)
    private int _building;

    // how long a build waits for the gate before it looks at its token again
    private const int GatePollMilliseconds = 50;

    // the initial capacity of the staging area of a build, the one SymSpell's own loaders use
    private const int StagingCapacity = 16384;

    internal TypoDictionary(NormalizationOptions normalization, Func<IEnumerable<(string Text, int Weight)>> source)
    {
        _normalization = normalization;
        _source = source;
    }

    /// <summary>Whether the dictionary has been built.</summary>
    internal bool IsBuilt => Volatile.Read(ref _symSpell) is not null;

    /// <summary>
    /// Where the dictionary stands (ADR-0025). A built dictionary is <c>Current</c> and only grows: a word that was removed
    /// stays until <see cref="Rebuild"/> or a restart — the index is the filter, a suggestion without a candidate falls away.
    /// </summary>
    internal LuiTypoDictionaryState State =>
        Volatile.Read(ref _building) > 0 ? LuiTypoDictionaryState.Building
        : !IsBuilt ? LuiTypoDictionaryState.NotBuilt
        : LuiTypoDictionaryState.Current;

    /// <summary>
    /// Builds the dictionary if it is not built and no other build runs; returns whether there is a dictionary to ask
    /// (ADR-0021). The build of a query:
    /// <list type="bullet">
    ///   <item>does not wait behind another build — the warm-up after the start, the build of another query: the query
    ///   gets <see langword="false"/> at once (review of pull request 23, S1). A rebuild of a built dictionary does not
    ///   count: the old one answers meanwhile;</item>
    ///   <item>is the index's, not the query's: it takes no token. An autocomplete cancels its request at the next
    ///   keystroke; a build bound to it would start anew and be thrown away each time, and never finish (review of pull
    ///   request 34, I3);</item>
    ///   <item>that fails gives <see langword="false"/> too, not an exception: the query answers without the correction,
    ///   and the next one that needs it tries again (I3). Only a catalog that is gone ends the query.</item>
    /// </list>
    /// </summary>
    internal bool TryEnsureBuilt()
    {
        if (IsBuilt)
        {
            return true;
        }

        if (!_buildGate.TryEnter())
        {
            return false;
        }

        try
        {
            if (!IsBuilt)
            {
                BuildAndPublish(CancellationToken.None);
            }

            return true;
        }
#pragma warning disable CA1031 // a failed build is a correction not searched, not a failed query (review of pull request 34, I3)
        catch (Exception exception) when (exception is not ObjectDisposedException)
#pragma warning restore CA1031
        {
            return false;
        }
        finally
        {
            _buildGate.Exit();
        }
    }

    /// <summary>Builds the dictionary from the content table, once; later calls return at once.</summary>
    /// <param name="cancellationToken">Ends the build, and the wait for a build that runs.</param>
    internal void EnsureBuilt(CancellationToken cancellationToken)
    {
        if (IsBuilt)
        {
            return;
        }

        EnterGate(cancellationToken);
        try
        {
            if (!IsBuilt)
            {
                BuildAndPublish(cancellationToken);
            }
        }
        finally
        {
            _buildGate.Exit();
        }
    }

    /// <summary>
    /// Builds the dictionary anew: the beginnings of words that were removed are gone, and the frequencies
    /// are the sums of the weights as they are now.
    /// </summary>
    /// <param name="cancellationToken">Ends the build, and the wait for a build that runs.</param>
    internal void Rebuild(CancellationToken cancellationToken)
    {
        EnterGate(cancellationToken);
        try
        {
            BuildAndPublish(cancellationToken);
        }
        finally
        {
            _buildGate.Exit();
        }
    }

    /// <summary>
    /// Takes the build gate, looking at the token while it waits: a host that stops is not held up by a build another
    /// thread runs (review of pull request 34, M3).
    /// </summary>
    private void EnterGate(CancellationToken cancellationToken)
    {
        while (!_buildGate.TryEnter(GatePollMilliseconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Adds the word beginnings of a text that are not in the dictionary yet. Nothing happens while there is no
    /// dictionary and none is being built: the build reads the table and finds the text there. While a build
    /// runs the text is queued for it — and, if there is a dictionary already, added to that one as well.
    /// </summary>
    internal void Add(string text, int weight)
    {
        lock (_pendingGate)
        {
            _pending?.Enqueue((text, weight));
        }

        // No dictionary yet: either none is wanted, or the build finds the text in the table — or replays the
        // queue above. The publish writes the dictionary and ends the queue in one step under the same gate, so
        // a write cannot fall between the two.
        if (Volatile.Read(ref _symSpell) is null)
        {
            return;
        }

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
    /// <see cref="Add"/> for many texts at once (<c>UpsertMany</c>): the queue gate and the write lock are taken
    /// once for all of them.
    /// </summary>
    internal void AddRange(IReadOnlyCollection<(string Text, int Weight)> texts)
    {
        if (texts.Count == 0)
        {
            return;
        }

        lock (_pendingGate)
        {
            if (_pending is { } pending)
            {
                foreach (var text in texts)
                {
                    pending.Enqueue(text);
                }
            }
        }

        if (Volatile.Read(ref _symSpell) is null)
        {
            return;
        }

        _lock.EnterWriteLock();
        try
        {
            if (_symSpell is { } symSpell)
            {
                foreach (var (text, weight) in texts)
                {
                    AddBeginnings(symSpell, text, weight, onlyNew: true);
                }
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
            if (Volatile.Read(ref _symSpell) is not { } symSpell)
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

    /// <summary>
    /// Builds a dictionary outside the lock, replays the writes that arrived meanwhile, swaps it in. Called
    /// with the build gate. A cancelled or failed build leaves the dictionary as it was.
    /// </summary>
    private void BuildAndPublish(CancellationToken cancellationToken)
    {
        lock (_pendingGate)
        {
            _pending = new Queue<(string, int)>();
        }

        Interlocked.Increment(ref _building);
        try
        {
            var fresh = Build(cancellationToken);

            _lock.EnterWriteLock();
            try
            {
                lock (_pendingGate)
                {
                    while (_pending!.TryDequeue(out var write))
                    {
                        AddBeginnings(fresh, write.Text, write.Weight, onlyNew: true);
                    }

                    // the dictionary first, then the end of the queue: a write sees one or the other
                    Volatile.Write(ref _symSpell, fresh);
                    _pending = null;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        catch
        {
            lock (_pendingGate)
            {
                _pending = null;
            }

            // a build that did not finish leaves the old dictionary as it was
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _building);
        }
    }

    private SymSpell Build(CancellationToken cancellationToken)
    {
        // one edit, the default prefix length; a count of at least 1 keeps every beginning
        var symSpell = new SymSpell(initialCapacity: 1 << 16, maxDictionaryEditDistance: 1, prefixLength: 7, countThreshold: 1);

        // The bulk path of the package, as its own LoadDictionary uses it: the delete lists are collected and put in place in
        // one step. 4.0 s → 2.3 s for the whole word list, a fifth less garbage, the same memory (#48).
        var stage = new SymSpell.SuggestionStage(StagingCapacity);
        foreach (var (text, weight) in _source())
        {
            // a volatile read per row: a build that is cancelled stops at the next row
            cancellationToken.ThrowIfCancellationRequested();

            AddBeginnings(symSpell, text, weight, onlyNew: false, stage);
        }

        symSpell.CommitStaged(stage);
        return symSpell;
    }

    private void AddBeginnings(SymSpell symSpell, string text, int weight, bool onlyNew, SymSpell.SuggestionStage? stage = null)
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
                symSpell.CreateDictionaryEntry(beginning, weight + 1L, stage);
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
