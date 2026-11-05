namespace Nordmann.LUI.Querying;

/// <summary>
/// The buffers of the candidates of a query, reused between the queries of a thread (issue #30).
/// <para>
/// A query that reads 500 candidates needs three arrays of 500 entries — 60 KB — and throws them away
/// again. They are the same size every time, so a thread keeps them: a query that finds its buffers
/// allocates nothing but its own texts. The arrays are cleared before they are returned, so they do not
/// hold the texts of the last query.
/// </para>
/// <para>
/// The length is always exact — a buffer is only lent to a query that wants exactly that length, so
/// that a buffer is never handed out twice. The length of the big buffers follows the budget of the
/// candidates, so a service that serves several budgets needs several lengths; the pool gives the
/// place of the smallest buffer up when there is no place left (ADR-0031).
/// </para>
/// </summary>
internal static class ScoredBuffer
{
    /// <summary>
    /// The number of buffers a thread keeps. A corrected query needs nine at the same time: the
    /// candidates of the input, the candidates of every correction (up to three), the buffer of the
    /// union, the buffer of what remains of it, the groups and the hits. With fewer slots one buffer
    /// per query would be allocated again — the union of 2,000 candidates is 80 KB.
    /// <para>
    /// Twelve are not enough for the budgets of a whole service: the length of the big buffers
    /// follows the budget of the candidates, so every new budget needs new buffers. When there is no
    /// place left, the smallest buffer goes (ADR-0031) — a pool that keeps the buffers of the
    /// past forever would make every query allocate anew, more than without a pool at all.
    /// </para>
    /// </summary>
    private const int Slots = 12;

    [ThreadStatic]
    private static Scored[]?[]? _slots;

    /// <summary>
    /// A buffer of exactly <paramref name="size"/> entries, empty.
    /// <para>
    /// The length is exact: the length of the array of the hits is the number of the hits, and a buffer
    /// that is too long would turn into hits that do not exist. Every buffer of a query has its own
    /// length — the candidates, the groups, the union — and a buffer is only used for the length it
    /// was made for: a buffer that is "good enough" for two lengths would be handed out twice.
    /// </para>
    /// </summary>
    internal static Scored[] Rent(int size)
    {
        var slots = _slots;
        if (slots is not null)
        {
            // a buffer of exactly the wanted length: every buffer of a query has its own length, and a
            // buffer that is "good enough" for two lengths would be handed out twice
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } pooled && pooled.Length == size)
                {
                    slots[i] = null;
                    return pooled;
                }
            }
        }

        return new Scored[size];
    }

    /// <summary>
    /// Gives a buffer back to the thread. When the pool is full, the smallest buffer makes room for it.
    /// </summary>
    internal static void Return(Scored[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);

        var slots = _slots ??= new Scored[]?[Slots];
        var smallest = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is null)
            {
                slots[i] = buffer;
                return;
            }

            // the smallest buffer is the one that goes when there is no place left: the big ones
            // follow the budget of the candidates and are the expensive ones (ADR-0031)
            if (smallest < 0 || slots[i]!.Length < slots[smallest]!.Length)
            {
                smallest = i;
            }
        }

        // No place left: the smallest buffer goes. A pool that keeps the buffers of the past
        // forever fills up with the lengths of the budgets that ran before — and then every query
        // allocates everything anew, more than without a pool at all.
        slots[smallest] = buffer;
    }
}
