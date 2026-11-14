#!/usr/bin/env python3
"""
Probe: die drei offenen Fragen des Lesepfads.

A) Eine ReadOnly-Verbindung auf einer WAL-Datenbank: kann sie lesen, wenn die -shm fehlt
   (der Writer ist weg, das -wal liegt noch da)?
B) Bringt das mmap auf einem lokalen Datentraeger ueberhaupt etwas? (Pragma mmap_size an/aus)
C) Was kostet das Neu-Vorbereiten einer Anweisung? (vorbereitete Anweisung wiederverwendet
   gegen jedes Mal neu vorbereitet) — die Frage nach der "geplanten Query" des Owners.

Schema und Korpus: das des Repos (200 000 Einträge aus words.txt.gz), gebaut von probes/probe_prefix_path.py.
"""

import os
import shutil
import sqlite3
import statistics
import subprocess
import sys
import time

SOURCE = "/tmp/lui_probe.db"
PREFIX = "SELECT s_rowid, text FROM lui_bench WHERE s_p1 = ? ORDER BY s_rowid LIMIT ?"
FTS = ("SELECT f.rowid, c.text FROM lui_bench_fts f JOIN lui_bench c ON c.doc_id = (f.rowid & 4294967295) "
       "WHERE lui_bench_fts MATCH ? ORDER BY f.rowid LIMIT ?")
HAUS = ('s_norm : "haus"', 501)


def pct(values, p):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(-(-len(ordered) * p // 100)) - 1)]


def median_ms(cursor, sql, params, runs=40):
    samples = []
    for _ in range(runs):
        started = time.perf_counter()
        cursor.execute(sql, params).fetchall()
        samples.append((time.perf_counter() - started) * 1000)
    return statistics.median(samples)


# --------------------------------------------------------------------------------------------------------------
# A) ReadOnly auf einer WAL-Datenbank, deren -shm fehlt
# --------------------------------------------------------------------------------------------------------------

print("=== A) eine ReadOnly-Verbindung auf einer WAL-Datenbank ohne -shm ===")

work = "/tmp/lui_ro.db"
for suffix in ("", "-wal", "-shm"):
    if os.path.exists(work + suffix):
        os.remove(work + suffix)
shutil.copy(SOURCE, work)

# ein Writer, der schreibt und sich ohne Checkpoint beendet (os._exit) -> das -wal bleibt liegen
writer = subprocess.run([sys.executable, "-c", f"""
import os, sqlite3
con = sqlite3.connect({work!r})
con.execute("PRAGMA journal_mode = WAL")
con.execute("PRAGMA wal_autocheckpoint = 0")
con.execute("INSERT INTO lui_bench(key, text, weight, priority) VALUES ('999999999', 'probe', 1, 3)")
con.commit()
os._exit(1)          # der Writer verschwindet, ohne zu schliessen: das -wal bleibt liegen
"""], capture_output=True)
print(f"  der Writer ist weg: -wal vorhanden = {os.path.exists(work + '-wal')}, "
      f"-shm vorhanden = {os.path.exists(work + '-shm')}")

if os.path.exists(work + "-shm"):
    os.remove(work + "-shm")
    print("  die -shm wurde entfernt (der letzte Leser muss sie neu anlegen)")

try:
    ro = sqlite3.connect(f"file:{work}?mode=ro", uri=True)
    rows = ro.execute(FTS, HAUS).fetchall()
    print(f"  ReadOnly, Verzeichnis schreibbar: {len(rows)} Zeilen gelesen  ✓  (-shm neu angelegt: "
          f"{os.path.exists(work + '-shm')})")
    ro.close()
except Exception as exception:
    print(f"  ReadOnly, Verzeichnis schreibbar: FEHLER {exception}")

# dieselbe Lage, aber die Verbindung darf nicht schreiben: query_only
try:
    normal = sqlite3.connect(work)
    normal.execute("PRAGMA query_only = ON")
    rows = normal.execute(FTS, HAUS).fetchall()
    print(f"  normale Verbindung + PRAGMA query_only=ON: {len(rows)} Zeilen gelesen  ✓")
    normal.close()
except Exception as exception:
    print(f"  normale Verbindung + PRAGMA query_only=ON: FEHLER {exception}")

# --------------------------------------------------------------------------------------------------------------
# B) mmap an/aus
# --------------------------------------------------------------------------------------------------------------

print("\n=== B) bringt das mmap etwas? (lokaler Datentraeger) ===")
for label, mmap in (("mit mmap_size = 2048 MB", 2147483648), ("ohne mmap", 0)):
    con = sqlite3.connect(work)
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA cache_size = -524288")
    if mmap:
        con.execute(f"PRAGMA mmap_size = {mmap}")
    cursor = con.cursor()
    for _ in range(3):
        cursor.execute(PREFIX, ("b", 501)).fetchall()
        cursor.execute(FTS, HAUS).fetchall()
    prefix = median_ms(cursor, PREFIX, ("b", 501))
    fts = median_ms(cursor, FTS, HAUS)
    print(f"  {label:26} Präfix 'b': {prefix:.3f} ms   FTS5 'haus': {fts:.3f} ms")
    con.close()

# --------------------------------------------------------------------------------------------------------------
# C) das Neu-Vorbereiten einer Anweisung
# --------------------------------------------------------------------------------------------------------------

print("\n=== C) was kostet das Neu-Vorbereiten? (cached_statements 128 gegen 0) ===")
for label, cached in (("Anweisung wiederverwendet (128)", 128), ("jedes Mal neu vorbereitet (0)", 0)):
    con = sqlite3.connect(work, cached_statements=cached)
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA cache_size = -524288")
    cursor = con.cursor()
    for _ in range(3):
        cursor.execute(PREFIX, ("b", 501)).fetchall()
        cursor.execute(FTS, HAUS).fetchall()
    prefix = median_ms(cursor, PREFIX, ("b", 501))
    fts = median_ms(cursor, FTS, HAUS)
    print(f"  {label:34} Präfix 'b': {prefix:.3f} ms   FTS5 'haus': {fts:.3f} ms")
    con.close()
