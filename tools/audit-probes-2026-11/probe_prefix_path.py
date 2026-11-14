#!/usr/bin/env python3
"""
Probe: the cost structure of LUI's two candidate paths, at the SQL level.

Schema and statements are taken from the repository (src/Nordmann.LUI/Schema/EmbeddedSource.cs,
src/Nordmann.LUI/Querying/CandidateReader.cs):
  - prefix path (1-2 characters): SELECT s_rowid, text FROM lui_bench WHERE s_p1 = $q ORDER BY s_rowid LIMIT $n
  - FTS5 path (3+ characters):    SELECT f.rowid, c.text FROM lui_bench_fts f JOIN lui_bench c
                                   ON c.doc_id = (f.rowid & 4294967295)
                                   WHERE lui_bench_fts MATCH $expr ORDER BY f.rowid LIMIT $n

The corpus is the repository's own file (tests/TestData/corpora/words.txt.gz), the first 200 000 entries.
Caveat: SQLite 3.40.1 (Debian 12) has no `remove_diacritics` option for the trigram tokenizer (SQLite 3.45+),
so the tokenizer here is plain 'trigram'. The plan and the index structure are unaffected by that option.
"""

import gzip
import os
import sqlite3
import statistics
import time

DB = "/tmp/lui_probe.db"
CORPUS = "/workspace/LUI/tests/TestData/corpora/words.txt.gz"
ENTRIES = 200_000
MAX_DOC_ID = 4294967295

SCHEMA = """
CREATE TABLE lui_bench (
    doc_id   INTEGER PRIMARY KEY CHECK (doc_id BETWEEN 0 AND 4294967295),
    key      TEXT    NOT NULL UNIQUE,
    text     TEXT    NOT NULL,
    weight   INTEGER NOT NULL CHECK (weight BETWEEN 0 AND 4294967295),
    priority INTEGER NOT NULL DEFAULT 3 CHECK (priority BETWEEN 0 AND 3),
    payload  BLOB,
    s_norm   TEXT GENERATED ALWAYS AS (lower(replace(replace(replace(text,'ä','ae'),'ö','oe'),'ü','ue'))) STORED,
    s_p1     TEXT GENERATED ALWAYS AS (substr(lower(replace(s_norm, '#', '')), 1, 1)) STORED,
    s_p2     TEXT GENERATED ALWAYS AS (substr(lower(replace(s_norm, '#', '')), 1, 2)) STORED,
    s_rowid  INTEGER GENERATED ALWAYS AS ((priority << 61) | ((4294967295 - weight) << 32) | doc_id) STORED
);
CREATE UNIQUE INDEX lui_bench_rowid ON lui_bench(s_rowid);
CREATE INDEX lui_bench_p1 ON lui_bench(s_p1, s_rowid);
CREATE INDEX lui_bench_p2 ON lui_bench(s_p2, s_rowid);
CREATE VIRTUAL TABLE lui_bench_fts USING fts5(
    s_norm,
    content = 'lui_bench', content_rowid = 's_rowid',
    tokenize = 'trigram');
CREATE TRIGGER lui_bench_ai AFTER INSERT ON lui_bench BEGIN
    INSERT INTO lui_bench_fts(rowid, s_norm) VALUES (new.s_rowid, new.s_norm);
END;
CREATE TRIGGER lui_bench_ad AFTER DELETE ON lui_bench BEGIN
    INSERT INTO lui_bench_fts(lui_bench_fts, rowid, s_norm) VALUES ('delete', old.s_rowid, old.s_norm);
END;
CREATE TRIGGER lui_bench_au AFTER UPDATE OF text, weight, priority ON lui_bench
WHEN old.s_rowid IS NOT new.s_rowid OR old.s_norm IS NOT new.s_norm
BEGIN
    INSERT INTO lui_bench_fts(lui_bench_fts, rowid, s_norm) VALUES ('delete', old.s_rowid, old.s_norm);
    INSERT INTO lui_bench_fts(rowid, s_norm) VALUES (new.s_rowid, new.s_norm);
END;
"""

PREFIX_WITH_TEXT = "SELECT s_rowid, text FROM lui_bench WHERE s_p1 = ? ORDER BY s_rowid LIMIT ?"
PREFIX_NO_TEXT = "SELECT s_rowid FROM lui_bench WHERE s_p1 = ? ORDER BY s_rowid LIMIT ?"
PREFIX2_WITH_TEXT = "SELECT s_rowid, text FROM lui_bench WHERE s_p2 = ? ORDER BY s_rowid LIMIT ?"
PREFIX2_NO_TEXT = "SELECT s_rowid FROM lui_bench WHERE s_p2 = ? ORDER BY s_rowid LIMIT ?"
FTS = ("SELECT f.rowid, c.text FROM lui_bench_fts f JOIN lui_bench c ON c.doc_id = (f.rowid & 4294967295) "
       "WHERE lui_bench_fts MATCH ? ORDER BY f.rowid LIMIT ?")


def build():
    if os.path.exists(DB):
        os.remove(DB)
    con = sqlite3.connect(DB)
    con.executescript(SCHEMA)
    rows = []
    with gzip.open(CORPUS, "rt", encoding="utf-8") as handle:
        # the header is a block of comment lines, ended by an empty line (ADR-0059)
        for line in handle:
            if not line.strip():
                break
        for line in handle:
            if not line.strip():
                continue
            text, weight = line.rstrip("\n").split("\t")
            rows.append((text, int(weight)))
            if len(rows) >= ENTRIES:
                break
    con.executemany("INSERT INTO lui_bench(doc_id, key, text, weight, priority) VALUES (?, ?, ?, ?, 3)",
                    [(i + 1, str(i + 1), text, weight) for i, (text, weight) in enumerate(rows)])
    con.commit()
    con.execute("ANALYZE")
    con.commit()
    con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    con.commit()
    print(f"built: {len(rows)} entries, file {os.path.getsize(DB) / 1048576:.0f} MB")
    return con


def plan(con, sql, params):
    print(f"\n--- EXPLAIN QUERY PLAN: {sql[:70]}...")
    for row in con.execute("EXPLAIN QUERY PLAN " + sql, params):
        print("   ", row[-1])


def timeit(con, sql, params, runs=30):
    samples = []
    rows = 0
    for _ in range(runs):
        started = time.perf_counter()
        result = con.execute(sql, params).fetchall()
        samples.append((time.perf_counter() - started) * 1000)
        rows = len(result)
    return statistics.median(samples), min(samples), rows


def main():
    con = build()
    con.execute("PRAGMA cache_size = -524288")   # 512 MB, as the benchmark's catalog
    con.execute("PRAGMA mmap_size = 2147483648")  # 2048 MB, as the benchmark's catalog

    for label, sql, params in (
        ("prefix 1 char, with the text (what LUI runs)", PREFIX_WITH_TEXT, ("b", 501)),
        ("prefix 1 char, only the rowid (what it could run)", PREFIX_NO_TEXT, ("b", 501)),
        ("prefix 2 chars, with the text (what LUI runs)", PREFIX2_WITH_TEXT, ("ka", 501)),
        ("prefix 2 chars, only the rowid (what it could run)", PREFIX2_NO_TEXT, ("ka", 501)),
        ("FTS5 path, 4 characters ('haus')", FTS, ('s_norm : "haus"', 501)),
        ("FTS5 path, 8 characters ('apfelkuc')", FTS, ('s_norm : "apfelkuc"', 501)),
    ):
        plan(con, sql, params)
        median, best, rows = timeit(con, sql, params)
        print(f"    -> median {median:.3f} ms, best {best:.3f} ms, {rows} rows")

    # how many rows does the FTS5 path really walk for a rare trigram?
    for expr in ('s_norm : "haus"', 's_norm : "apfelkuc"'):
        count = con.execute("SELECT count(*) FROM lui_bench_fts WHERE lui_bench_fts MATCH ?", (expr,)).fetchone()[0]
        print(f"\nFTS5 hits for {expr}: {count}")

    # the hits statement: the key and the payload of the hits
    hits_sql = "SELECT doc_id, key, payload FROM lui_bench WHERE doc_id IN (?, ?, ?, ?)"
    median, best, rows = timeit(con, hits_sql, (1, 2, 3, 4))
    print(f"\nhits statement (4 rows): median {median:.3f} ms, {rows} rows")


if __name__ == "__main__":
    main()
