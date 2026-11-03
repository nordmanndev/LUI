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
        var multi = definition.Kind == LuiIndexKind.MultiWord;

        // The column the search index holds: s_norm for a single-word index; for a multi-word index
        // s_compact alone (ADR-0011) — it holds everything a multi-word query asks, and s_norm, which
        // stays a column of the table, would only store every title a second time.
        var column = multi ? "s_compact" : "s_norm";

        return
        [
            ContentTable(content, definition),
            $"CREATE UNIQUE INDEX {content}_rowid ON {content}(s_rowid);",
            $"CREATE INDEX {content}_p1 ON {content}(s_p1, s_rowid);",
            $"CREATE INDEX {content}_p2 ON {content}(s_p2, s_rowid);",
            $"""
            CREATE VIRTUAL TABLE {fts} USING fts5(
                {column},
                content = '{content}', content_rowid = 's_rowid',
                tokenize = 'trigram remove_diacritics 1');
            """,
            .. Triggers(content, fts, column),
        ];
    }

    private static string ContentTable(string content, IndexDefinition definition)
    {
        var multi = definition.Kind == LuiIndexKind.MultiWord;

        // The prefix columns hold the first letters of the text a query is read against: s_norm without its
        // '#' for a single-word index; s_compact — no spaces, no punctuation, no '#' — for a multi-word index
        // (§4: „spaces do not matter", review of pull request 8). lower() folds ASCII only (§6.2).
        var prefixSource = multi ? "s_compact" : "replace(s_norm, '#', '')";

        // The compact column strips the separators from the transliterated text (§6.1); the tokenizer
        // folds it, as it folds s_norm. Only a multi-word index has it (§7.2). There s_norm is a VIRTUAL
        // column: nothing reads it but s_compact, which derives from it, and a stored copy of every title
        // is a tenth of the file.
        var norm = multi ? "VIRTUAL" : "STORED";
        var compact = multi
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
                s_norm   TEXT GENERATED ALWAYS AS ({SqlNormalizer.Transliteration("text", definition.GermanTransliteration)}) {norm}{compact},
                s_p1     TEXT GENERATED ALWAYS AS (substr(lower({prefixSource}), 1, 1)) STORED,
                s_p2     TEXT GENERATED ALWAYS AS (substr(lower({prefixSource}), 1, 2)) STORED,
                s_rowid  INTEGER GENERATED ALWAYS AS ((priority << 61) | (({RowId.MaxWeight} - weight) << 32) | doc_id) STORED
            );
            """;
    }

    /// <summary>The triggers; <paramref name="column"/> is the one column the search index holds.</summary>
    private static string[] Triggers(string content, string fts, string column)
    {
        var added = $"new.{column}";
        var removed = $"old.{column}";

        return
        [
            $"""
            CREATE TRIGGER {content}_ai AFTER INSERT ON {content} BEGIN
                INSERT INTO {fts}(rowid, {column}) VALUES (new.s_rowid, {added});
            END;
            """,
            $"""
            CREATE TRIGGER {content}_ad AFTER DELETE ON {content} BEGIN
                INSERT INTO {fts}({fts}, rowid, {column}) VALUES ('delete', old.s_rowid, {removed});
            END;
            """,

            // Only a change of the rowid or of the indexed text reaches the search index: an identical
            // upsert and a change of the payload alone leave it alone (review P1) — every touch is a
            // delete and an insert, and a new small segment. For a multi-word index only a change of the
            // compact text counts: „Apfel Kuchen" to „Apfel-Kuchen" is none.
            $"""
            CREATE TRIGGER {content}_au AFTER UPDATE OF text, weight, priority ON {content}
            WHEN old.s_rowid IS NOT new.s_rowid OR old.{column} IS NOT new.{column}
            BEGIN
                INSERT INTO {fts}({fts}, rowid, {column}) VALUES ('delete', old.s_rowid, {removed});
                INSERT INTO {fts}(rowid, {column}) VALUES (new.s_rowid, {added});
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
