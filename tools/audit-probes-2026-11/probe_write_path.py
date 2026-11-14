#!/usr/bin/env python3
"""
Probe: what a single write costs in LUI, and what the FTS5 trigger costs.

The schema is the repository's own (src/Nordmann.LUI/Schema/EmbeddedSource.cs):
  s_rowid = (priority << 61) | ((MaxWeight - weight) << 32) | doc_id
  the trigger lui_bench_au fires when old.s_rowid IS NOT new.s_rowid OR the indexed text changed

A change of the weight therefore changes s_rowid — and s_rowid is the rowid of the FTS5 table. Every change
of a weight is a delete and an insert in the search index, not a metadata update.

The writes are the ones of the budget test (tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs,
MeasureWrites): 600 writes, a third an upsert, a third a weight, a third a removal and the entry back.
"""

import os
import shutil
import sqlite3
import statistics
import time

SOURCE = "/tmp/lui_probe.db"
WRITES = 600
LIMIT = 501


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


def measure(con, label):
    """The 600 writes of the budget test, each in its own transaction (autocommit)."""
    latencies = []
    for i in range(WRITES):
        key = (i * 7919) % 200_000 + 1
        started = time.perf_counter()
        if i % 3 == 0:
            con.execute("INSERT INTO lui_bench(key, text, weight, priority) VALUES (?, ?, ?, 3) "
                        "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
                        "priority = excluded.priority", (str(key), f"probe text {key}", (i * 7919) % 1000))
        elif i % 3 == 1:
            con.execute("UPDATE lui_bench SET weight = ? WHERE key = ?", ((i * 7919) % 1000, str(key)))
        else:
            con.execute("DELETE FROM lui_bench WHERE key = ?", (str(key),))
            con.execute("INSERT INTO lui_bench(key, text, weight, priority) VALUES (?, ?, ?, 3) "
                        "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
                        "priority = excluded.priority", (str(key), f"probe text {key}", 999))
        latencies.append((time.perf_counter() - started) * 1000)
    print(f"{label}: p50 {percentile(latencies, 50):.3f} ms, p95 {percentile(latencies, 95):.3f} ms, "
          f"max {max(latencies):.3f} ms, mean {statistics.mean(latencies):.3f} ms")
    return latencies


def single(con, sql, params, label):
    samples = []
    for i in range(40):
        started = time.perf_counter()
        con.execute(sql, params)
        samples.append((time.perf_counter() - started) * 1000)
    print(f"  {label}: median {statistics.median(samples):.3f} ms")


def main():
    work = "/tmp/lui_probe_write.db"
    shutil.copy(SOURCE, work)
    con = open_db(work)

    print("=== with the triggers of the repository ===")
    measure(con, "600 mixed writes")
    single(con, "UPDATE lui_bench SET weight = ? WHERE key = ?", (12345, str(7)), "one weight change (the trigger fires)")
    single(con, "UPDATE lui_bench SET weight = ? WHERE key = ?", (12345, str(8)), "the same weight again (the trigger does not fire)")
    single(con, "UPDATE lui_bench SET text = text WHERE key = ?", (str(9),), "the same text again (the trigger does not fire)")
    single(con, "UPDATE lui_bench SET priority = priority WHERE key = ?", (str(10),), "the same priority (the trigger does not fire)")

    print("\n=== without the update trigger (the FTS5 rewrite of the rowid) ===")
    con.execute("DROP TRIGGER lui_bench_au")
    con.commit()
    measure(con, "600 mixed writes")
    single(con, "UPDATE lui_bench SET weight = ? WHERE key = ?", (54321, str(7)), "one weight change (the trigger is gone)")

    print("\n=== the state of the file ===")
    for suffix in ("", "-wal"):
        path = work + suffix
        if os.path.exists(path):
            print(f"  {os.path.basename(path)}: {os.path.getsize(path) / 1048576:.1f} MB")


if __name__ == "__main__":
    main()
