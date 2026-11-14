#!/usr/bin/env python3
"""
Probe: the three single writes of the budget test, one by one.

The writes are the ones of tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs (MeasureWrites),
against the schema of src/Nordmann.LUI/Schema/EmbeddedSource.cs:
  A  upsert of the SAME text with a new weight   -> the trigger fires (s_rowid changed), the text is the same
  B  a new weight                                -> the trigger fires (s_rowid changed)
  C  a removal and the entry back                -> the delete and the insert trigger fire

The text written is the text of the entry — as the budget test does — and not a new one, so that the
measurement is the one the budget measures.
"""

import gzip
import os
import shutil
import sqlite3
import statistics
import time

SOURCE = "/tmp/lui_probe.db"
CORPUS = "/workspace/LUI/tests/TestData/corpora/words.txt.gz"
ENTRIES = 200_000


def texts():
    result = []
    with gzip.open(CORPUS, "rt", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                break
        for line in handle:
            if not line.strip():
                continue
            text, _ = line.rstrip("\n").split("\t")
            result.append(text)
            if len(result) >= ENTRIES:
                break
    return result


def open_db(path):
    con = sqlite3.connect(path)
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA synchronous = NORMAL")
    con.execute("PRAGMA busy_timeout = 5000")
    con.execute("PRAGMA cache_size = -524288")
    con.execute("PRAGMA mmap_size = 2147483648")
    return con


def percentile(values, p):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(-(-len(ordered) * p // 100)) - 1)]


def write_a(con, key, text, weight):
    con.execute("INSERT INTO lui_bench(key, text, weight, priority) VALUES (?, ?, ?, 3) "
                "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
                "priority = excluded.priority", (str(key), text, weight))


def write_b(con, key, weight):
    con.execute("UPDATE lui_bench SET weight = ? WHERE key = ?", (weight, str(key)))


def write_c(con, key, text, weight):
    con.execute("DELETE FROM lui_bench WHERE key = ?", (str(key),))
    con.execute("INSERT INTO lui_bench(key, text, weight, priority) VALUES (?, ?, ?, 3) "
                "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
                "priority = excluded.priority", (str(key), text, weight))


def measure(con, corpus, label):
    classes = {"A upsert, same text, new weight": [], "B new weight": [], "C removal and back": []}
    for i in range(300):
        key = (i * 7919) % ENTRIES + 1
        text = corpus[key - 1]
        weight = (i * 7919) % 1000
        kind = i % 3
        started = time.perf_counter()
        if kind == 0:
            write_a(con, key, text, weight)
        elif kind == 1:
            write_b(con, key, weight)
        else:
            write_c(con, key, text, 999)
        elapsed = (time.perf_counter() - started) * 1000
        classes["A upsert, same text, new weight" if kind == 0 else
                "B new weight" if kind == 1 else "C removal and back"].append(elapsed)
    print(f"\n{label}")
    for name, values in classes.items():
        print(f"  {name}: p50 {percentile(values, 50):.3f} ms, p95 {percentile(values, 95):.3f} ms, "
              f"max {max(values):.3f} ms")
    all_values = [value for values in classes.values() for value in values]
    print(f"  all three classes: p50 {percentile(all_values, 50):.3f} ms, p95 {percentile(all_values, 95):.3f} ms")


def main():
    corpus = texts()
    work = "/tmp/lui_probe_write2.db"
    shutil.copy(SOURCE, work)
    con = open_db(work)
    print("=== with the triggers of the repository ===")
    measure(con, corpus, "300 writes, three classes")
    single = []
    for i in range(40):
        started = time.perf_counter()
        write_b(con, (i * 7919) % ENTRIES + 1, (i * 31) % 1000)
        single.append((time.perf_counter() - started) * 1000)
    print(f"  one weight change alone: p50 {percentile(single, 50):.3f} ms, p95 {percentile(single, 95):.3f} ms")

    print("\n=== without the update trigger (no rewrite of the FTS5 rowid) ===")
    con.execute("DROP TRIGGER lui_bench_au")
    con.commit()
    measure(con, corpus, "300 writes, three classes")
    single = []
    for i in range(40):
        started = time.perf_counter()
        write_b(con, (i * 7919) % ENTRIES + 1, (i * 31) % 1000)
        single.append((time.perf_counter() - started) * 1000)
    print(f"  one weight change alone: p50 {percentile(single, 50):.3f} ms, p95 {percentile(single, 95):.3f} ms")


if __name__ == "__main__":
    main()
