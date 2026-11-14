#!/usr/bin/env python3
"""
Probe: was passiert, wenn LUI das WAL-Schreiben nicht mehr selbst setzt?

Der Lesepfad haelt einen Momentaufnahme (eine Transaktion) fuer Kandidaten und Treffer
("one snapshot for the candidates and the hits"). Im WAL-Modus stoeren sich Leser und Schreiber nicht;
im Rollback-Journal (SQLites Standard) haelt der Leser eine SHARED-Sperre, bis die Transaktion endet -
der Schreiber wartet dann auf den Leser.

Gemessen: ein Leser mit offener Transaktion, ein Schreiber daneben, busy_timeout 1000 ms,
in beiden Journal-Modi.
"""

import os
import sqlite3
import threading
import time

WORKER = """
import os, sqlite3, time
path = {path!r}
con = sqlite3.connect(path, timeout=1.0)
con.execute("PRAGMA busy_timeout = 1000")
started = time.perf_counter()
try:
    con.execute("INSERT INTO t (name) VALUES ('writer')")
    con.commit()
    print(f"  der Schreiber warte {{(time.perf_counter() - started) * 1000:.1f}} ms  und kam durch")
except Exception as exception:
    print(f"  der Schreiber wartete {{(time.perf_counter() - started) * 1000:.1f}} ms  und scheiterte: {{exception}}")
"""


def run(journal):
    path = f"/tmp/lui_journal_{journal}.db"
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(path + suffix):
            os.remove(path + suffix)

    con = sqlite3.connect(path)
    con.execute(f"PRAGMA journal_mode = {journal};")
    con.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);")
    con.execute("INSERT INTO t (name) VALUES ('reader')")
    con.commit()

    # der Leser: eine Momentaufnahme, offen
    reader = sqlite3.connect(path, timeout=1.0)
    reader.execute("PRAGMA busy_timeout = 1000")
    reader.execute("BEGIN")
    reader.execute("SELECT count(*) FROM t").fetchone()

    print(f"=== journal_mode = {journal} ===")
    writer = threading.Thread(target=lambda: exec(WORKER.format(path=path)))
    writer.start()
    writer.join()

    reader.execute("COMMIT")
    reader.close()
    con.close()
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(path + suffix):
            os.remove(path + suffix)


run("WAL")
run("DELETE")
