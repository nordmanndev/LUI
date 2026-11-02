namespace Nordmann.LUI.Indexing;

/// <summary>The write statements of an <c>Owned</c> index (LUI-Spezifikation §12.4).</summary>
internal sealed class OwnedSql(string name)
{
    private readonly string _content = OwnedSchema.Content(name);

    internal string Upsert =>
        $"INSERT INTO {_content}(key, text, weight, priority, payload) "
        + "VALUES ($key, $text, $weight, $priority, $payload) "
        + "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
        + "priority = excluded.priority, payload = excluded.payload;";

    internal string UpdateWeight => $"UPDATE {_content} SET weight = $weight WHERE key = $key;";

    internal string UpdatePriority => $"UPDATE {_content} SET priority = $priority WHERE key = $key;";

    internal string Remove => $"DELETE FROM {_content} WHERE key = $key;";

    internal string Count => $"SELECT count(*) FROM {_content};";
}
