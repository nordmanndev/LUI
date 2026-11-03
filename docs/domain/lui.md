# Domain context — LUI

> The design before the code. Anyone may edit this file; the agent typically
> drafts the inventory, the domain expert refines the language. This file and
> [`../spec/LUI-Spezifikation.md`](../spec/LUI-Spezifikation.md) are the two
> artifacts: this one is the *context*, the specification is the *contract*.
> Where they disagree, the specification wins and this file gets corrected.

---

## 1. What this context is

LUI answers one question: **which entries should a search field suggest?**

It does that against a stock of short texts — tags, words, names, recipe and
book titles — that live in a SQLite database the application already owns.
The stock can hold millions of entries; the answer is the best handful.

The user feels four rules, and only four:

1. **One or two letters in, the most popular thing comes first.** There is
   not enough information to be clever.
2. **What they typed is the beginning of a word** — that comes first.
3. **Among those, the best match wins**; on a tie, the more popular one.
4. **Spaces, word order and umlaut spelling do not matter.** A typo does not
   lead into the void.

Everything else in LUI is machinery for those four rules.

## 2. The language

| Term | Meaning |
|---|---|
| **Catalog** (`LuiCatalog`) | The entry point. Knows the connection string and every index of one database. |
| **Index** | A searchable stock with a name, exactly one kind and exactly one content source. |
| **Entry** | A searchable element: key, text, weight, priority, optional payload. |
| **Key** | The entry's identity *in the application* — a recipe id, a tag id. Not LUI's business. |
| **Text** | What is searched and shown: „Gedeckter Apfelkuchen". |
| **Weight** | Popularity, `0 … 536 870 911`. Decides *which* entries become candidates, and breaks ties. |
| **Priority** | A class `0 … 3` in front of the weight: `System`, `High`, `Elevated`, `Normal`. Higher priority entries are candidates **first**. |
| **Payload** | Data that comes back with a hit (slug, colour, icon). Read only for the hits, never for all candidates. |
| **Kind** (`LuiIndexKind`) | `SingleWord` or `MultiWord`. Fixed at creation; decides how searching works. |
| **Content source** | `Owned` (the library's own table) or `Linked` (a table of the application). |
| **Candidate** | An entry the search proposes, before sorting. At most `Candidates` (default 500). |
| **Hit** | A candidate that made it into the top `MaxResults` (default 20). |
| **Word beginning** | The input is the beginning of one of the entry's words. |

## 3. The invariants

These never break. Each of them has a test.

1. **The search index and the content table change in one transaction.**
   Triggers do it; a rollback rolls back both.
2. **Priority before weight, always.** A `System` entry that matches is a
   candidate *first*, even when 33 000 other entries match (Anhang A, A8).
3. **The normalisation rule is the same on both sides.** The SQL columns and
   the C# normaliser produce the same search form — for every word of the
   German word list, not for a sample of it (§6.2, §19).
4. **A query never mutates.** Not the data, not the index, not the schema.
5. **An index is never silently rebuilt.** A definition that differs from
   `lui_meta` is an error that names both definitions.
6. **Identifiers never reach SQL unchecked.** Every name is validated against
   `^[a-z][a-z0-9_]{0,40}$` before it is interpolated.
7. **The query API is synchronous** (§13). `Microsoft.Data.Sqlite` has no
   asynchronous I/O; a `Task` would be a lie.

## 4. The data model

```text
lui_meta          one row per index: kind, source, definition (JSON), schema version
lui_<index>       the content table (Owned) — doc_id, key, text, weight, priority, payload
                  plus the generated columns s_norm, s_compact, s_p1, s_p2, s_rowid
lui_<index>_fts   the FTS5 virtual table (external content, trigram tokenizer)
                  plus three triggers: insert, update, delete
```

The generated column `s_rowid` packs priority, weight and document id into
one 64-bit number, so that "highest priority first, then most popular first"
is the *order of the row ids* — no sort step, no temporary B-tree.

In `Linked` mode the content table is the application's own, and LUI does not
change it (ADR-0015): no column, no index, no trigger unless the application
asks for one. LUI keeps a table without text and a contentless search index of
its own, and reads text and payload from the application's table by its
primary key.

## 5. The flows

**The query.** Normalise → too short? use the prefix path → otherwise ask
FTS5 → no candidate starts a word? correct the typo and try again → rank →
read the payloads of the hits → answer.

**The write (Owned).** `Upsert` / `UpdateWeight` / `UpdatePriority` /
`Remove` — one statement each, inside the application's transaction when the
application has one.

**The write (Linked).** The application writes its own table and tells the
index: `Refresh(key)` after a write (in its transaction, if it likes),
`Sync()` for everything — or triggers it created on purpose. LUI has no write
API for the content of a linked index, on purpose: the table is the content.

**The reconciliation.** `Sync` compares the content table with a source in
batches, adds what is missing, changes what differs, removes the keys that
were in the index when the sync began and that the source does not have — a
key the application adds meanwhile stays. It is cancellable and resumable: a second run
skips what the first committed (ADR-0016).

**The repair.** `Check(source)` compares the content table with a source,
`Check()` verifies the definition, the triggers and the FTS5 index (for a
linked index also the entries against the table's rows), `Rebuild` rebuilds the
triggers and the search index from the content table, `Optimize` merges it.
Each finding of a check names its repair.

## 6. The extension points

| Extension | What the application provides | What LUI does with it |
|---|---|---|
| Payload (Owned) | a `JsonTypeInfo<T>` or an `IPayloadSerializer<T>` | serialises it, stores it, returns it for the hits |
| Payload (Linked) | a mapping from the table's columns | reads the columns for the hits |
| Similarity | `ILuiSimilarity` | measures the candidates |
| Content source (Sync) | a sequence of entries | reconciles the content table |

## 7. Out of scope for 1.0

Everything in §21 of the specification, with the reason and the revival
condition in [ADR-0003](../adr/0003-defer-the-open-questions.md): multi-word
prefix matching (O1), virtual normalised columns (O2), hashtags as word
beginnings (O3), weight expressions (O4), languages beyond German (O5),
filters on linked indices (O6), soft ranking (O7), suggestions for an empty
input (O8), more than four priority classes (O9).
