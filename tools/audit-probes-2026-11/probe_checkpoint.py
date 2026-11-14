#!/usr/bin/env python3
"""
Probe: how long a write series has to be before the WAL checkpoint shows up.

The budget test measures 600 writes (tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs, MeasureWrites)
and takes the 95th percentile. BenchmarkDotNet runs the same operation thousands of times and reports a mean
of 1.87 ms (2026-11-06, Xeon 6982P-C) and 2.04 ms (2026-11-13, Xeon Platinum).

Hypothesis: the difference is the checkpoint. SQLite checkpoints automatically every 1000 pages of the WAL
(PRAGMA wal_autocheckpoint). Each checkpoint copies the WAL into the database file and fsyncs it — a cost that
lands on the writer. 600 writes are far too few to reach a p95 that shows it.

The probe: the same operation (a change of a weight, which rewrites the rowid of the FTS5 table) in series
of growing length, once with the default checkpoint interval and once with a large one.
"""

import os
import shutil
import sqlite3
import statistics
import time

SOURCE = "/tmp/lui_probe.db"
ENTRIES = 200_000


def percentile(values, p):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(-(-len(ordered) * p // 100)) - 1)]


def run(count, autocheckpoint):
    work = "/tmp/lui_probe_ckpt.db"
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(work + suffix):
            os.remove(work + suffix)
    shutil.copy(SOURCE, work)
    con = sqlite3.connect(work)
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA synchronous = NORMAL")
    con.execute("PRAGMA busy_timeout = 5000")
    con.execute("PRAGMA cache_size = -524288")
    con.execute("PRAGMA mmap_size = 2147483648")
    if autocheckpoint:
        con.execute(f"PRAGMA wal_autocheckpoint = {autocheckpoint}")

    latencies = []
    for i in range(count):
        key = (i * 7919) % ENTRIES + 1
        started = time.perf_counter()
        con.execute("UPDATE lui_bench SET weight = ? WHERE key = ?", ((i * 31) % 1000, str(key)))
        latencies.append((time.perf_counter() - started) * 1000)

    wal = os.path.getsize(work + "-wal") / 1048576 if os.path.exists(work + "-wal") else 0
    print(f"  {count:>6} writes, wal_autocheckpoint={autocheckpoint or 'default (1000 pages)'}: "
          f"p50 {percentile(latencies, 50):.3f} ms, p95 {percentile(latencies, 95):.3f} ms, "
          f"p99 {percentile(latencies, 99):.3f} ms, max {max(latencies):.3f} ms, "
          f"mean {statistics.mean(latencies):.3f} ms, WAL {wal:.1f} MB")
    con.close()


def main():
    print("=== the same operation in series of growing length ===")
    for count in (600, 5_000, 20_000):
        run(count, None)

    print("\n=== the same, with the checkpoint interval raised (ADR-0016 measured 12 % for it) ===")
    for count in (600, 5_000, 20_000):
        run(count, 100_000)


if __name__ == "__main__":
    main()
