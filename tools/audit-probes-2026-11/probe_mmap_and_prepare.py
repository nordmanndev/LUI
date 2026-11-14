#!/usr/bin/env python3
"""
Probe (Nachtrag): zwei Entscheidungen mit gepaarten Messungen.

1) mmap auf einem Netzwerkdateisystem: ist das der Grund, warum die Reads dort 7x langsamer sind?
2) Das Neu-Vorbereiten einer Anweisung je Anfrage: gepaart und in wechselnder Reihenfolge gemessen
   (die Regel des Repos: "old and new are measured alternately, and the median is taken").
"""

import os
import shutil
import sqlite3
import statistics
import time

SOURCE = "/tmp/lui_probe.db"
PREFIX = "SELECT s_rowid, text FROM lui_bench WHERE s_p1 = ? ORDER BY s_rowid LIMIT ?"
FTS = ("SELECT f.rowid, c.text FROM lui_bench_fts f JOIN lui_bench c ON c.doc_id = (f.rowid & 4294967295) "
       "WHERE lui_bench_fts MATCH ? ORDER BY f.rowid LIMIT ?")
HAUS = ('s_norm : "haus"', 501)


def measure(cursor, sql, params, runs=40):
    samples = []
    for _ in range(runs):
        started = time.perf_counter()
        cursor.execute(sql, params).fetchall()
        samples.append((time.perf_counter() - started) * 1000)
    return statistics.median(samples)


def open_at(path, mmap, cached=128):
    con = sqlite3.connect(path, cached_statements=cached)
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA cache_size = -524288")
    if mmap:
        con.execute(f"PRAGMA mmap_size = {mmap}")
    return con


print("=== 1) mmap an/aus, lokal gegen NAS ===")
os.makedirs("/workspace/output/probes/tmp", exist_ok=True)
for where, label in (("/tmp", "lokale Platte"), ("/workspace/output/probes/tmp", "NAS-Mount")):
    target = os.path.join(where, "lui_mmap.db")
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(target + suffix):
            os.remove(target + suffix)
    shutil.copy(SOURCE, target)
    for mmap in (2147483648, 0):
        con = open_at(target, mmap)
        cursor = con.cursor()
        for _ in range(5):
            cursor.execute(PREFIX, ("b", 501)).fetchall()
            cursor.execute(FTS, HAUS).fetchall()
        prefix = measure(cursor, PREFIX, ("b", 501))
        fts = measure(cursor, FTS, HAUS)
        print(f"  {label:14} mmap {'an ' if mmap else 'aus'}: Präfix 'b' {prefix:7.3f} ms   FTS5 'haus' {fts:7.3f} ms")
        con.close()
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(target + suffix):
            os.remove(target + suffix)

print("\n=== 2) das Neu-Vorbereiten, gepaart und in wechselnder Reihenfolge ===")
for sql, params, name in ((PREFIX, ("b", 501), "Präfix 'b'"), (FTS, HAUS, "FTS5 'haus'")):
    reused, prepared = [], []
    for round_index in range(6):
        order = (("reused", reused), ("prepared", prepared))
        if round_index % 2 == 1:
            order = tuple(reversed(order))
        for label, sink in order:
            cached = 128 if label == "reused" else 0
            con = open_at(SOURCE, 0, cached=cached)
            cursor = con.cursor()
            for _ in range(3):
                cursor.execute(sql, params).fetchall()
            sink.append(measure(cursor, sql, params, runs=20))
            con.close()
    print(f"  {name:12} wiederverwendet {statistics.median(reused):7.3f} ms   "
          f"jedes Mal neu vorbereitet {statistics.median(prepared):7.3f} ms   "
          f"-> das Vorbereiten kostet {statistics.median(prepared) - statistics.median(reused):+.3f} ms")
