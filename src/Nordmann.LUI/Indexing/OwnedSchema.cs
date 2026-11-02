using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// The SQL of an <c>Owned</c> index: the content table with its generated
/// columns, the three indexes, the FTS5 table and the three triggers
/// (LUI-Spezifikation §7.2, §7.4). Every name that reaches this class has been
/// checked against <see cref="Identifier.Pattern"/>.
/// </summary>
internal static class OwnedSchema
{
    internal const int SchemaVersion = 1;

    internal const string Source = "Owned";

    internal const string MetaTable =
        """
        CREATE TABLE IF NOT EXISTS lui_meta (
            index_name     TEXT PRIMARY KEY,
            kind           TEXT NOT NULL,
            source         TEXT NOT NULL,
            definition     TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            created_at     TEXT NOT NULL
        );
        """;

    internal static string Content(string name) => $"lui_{name}";

    internal static string Fts(string name) => $"lui_{name}_fts";

    /// <summary>The statements that create an index, in the order they have to run.</summary>
    internal static IReadOnlyList<string> Create(string name, IndexDefinition definition)
    {
        var content = Content(name);
        var fts = Fts(name);
        var multi = definition.Kind == IndexKind.MultiWord;

        // the columns the search index holds: s_compact only for a multi-word index (§7.2)
        var columns = multi ? "s_norm, s_compact" : "s_norm";

        return
        [
            ContentTable(content, definition),
            $"CREATE UNIQUE INDEX {content}_rowid ON {content}(s_rowid);",
            $"CREATE INDEX {content}_p1 ON {content}(s_p1, s_rowid);",
            $"CREATE INDEX {content}_p2 ON {content}(s_p2, s_rowid);",
            $"""
            CREATE VIRTUAL TABLE {fts} USING fts5(
                {columns},
                content = '{content}', content_rowid = 's_rowid',
                tokenize = 'trigram remove_diacritics 1');
            """,
            .. Triggers(content, fts, columns, multi),
        ];
    }

    private static string ContentTable(string content, IndexDefinition definition)
    {
        // The prefix columns drop the '#' of a hashtag; lower() folds ASCII only (§6.2).
        const string withoutHash = "replace(s_norm, '#', '')";

        // The compact column strips the separators from the transliterated text (§6.1); the tokenizer
        // folds it, as it folds s_norm. Only a multi-word index has it (§7.2).
        var compact = definition.Kind == IndexKind.MultiWord
            ? $",\n    s_compact TEXT GENERATED ALWAYS AS ({SqlNormalizer.Compact("s_norm")}) STORED"
            : string.Empty;

        return $"""
            CREATE TABLE {content} (
                doc_id   INTEGER PRIMARY KEY CHECK (doc_id BETWEEN 0 AND {RowId.MaxDocId}),
                key      TEXT    NOT NULL UNIQUE,
                text     TEXT    NOT NULL,
                weight   INTEGER NOT NULL CHECK (weight BETWEEN 0 AND {RowId.MaxWeight}),
                priority INTEGER NOT NULL DEFAULT 3 CHECK (priority BETWEEN 0 AND 3),
                payload  BLOB,
                s_norm   TEXT GENERATED ALWAYS AS ({SqlNormalizer.Transliteration("text", definition.GermanTransliteration)}) STORED{compact},
                s_p1     TEXT GENERATED ALWAYS AS (substr(lower({withoutHash}), 1, 1)) STORED,
                s_p2     TEXT GENERATED ALWAYS AS (substr(lower({withoutHash}), 1, 2)) STORED,
                s_rowid  INTEGER GENERATED ALWAYS AS ((priority << 61) | (({RowId.MaxWeight} - weight) << 32) | doc_id) STORED
            );
            """;
    }

    private static string[] Triggers(string content, string fts, string columns, bool multi)
    {
        var added = multi ? "new.s_norm, new.s_compact" : "new.s_norm";
        var removed = multi ? "old.s_norm, old.s_compact" : "old.s_norm";

        return
        [
            $"""
            CREATE TRIGGER {content}_ai AFTER INSERT ON {content} BEGIN
                INSERT INTO {fts}(rowid, {columns}) VALUES (new.s_rowid, {added});
            END;
            """,
            $"""
            CREATE TRIGGER {content}_ad AFTER DELETE ON {content} BEGIN
                INSERT INTO {fts}({fts}, rowid, {columns}) VALUES ('delete', old.s_rowid, {removed});
            END;
            """,

            // Only a change of the rowid or of the indexed text reaches the search index: an identical
            // upsert and a change of the payload alone leave it alone (review P1) — every touch is a
            // delete and an insert, and a new small segment. s_compact is derived from s_norm.
            $"""
            CREATE TRIGGER {content}_au AFTER UPDATE OF text, weight, priority ON {content}
            WHEN old.s_rowid IS NOT new.s_rowid OR old.s_norm IS NOT new.s_norm
            BEGIN
                INSERT INTO {fts}({fts}, rowid, {columns}) VALUES ('delete', old.s_rowid, {removed});
                INSERT INTO {fts}(rowid, {columns}) VALUES (new.s_rowid, {added});
            END;
            """,
        ];
    }

    /// <summary>The statements that remove an index, in the order they have to run.</summary>
    internal static IReadOnlyList<string> Drop(string name)
    {
        var content = Content(name);

        return
        [
            $"DROP TRIGGER IF EXISTS {content}_ai;",
            $"DROP TRIGGER IF EXISTS {content}_ad;",
            $"DROP TRIGGER IF EXISTS {content}_au;",
            $"DROP TABLE IF EXISTS {Fts(name)};",
            $"DROP TABLE IF EXISTS {content};",
        ];
    }
}
