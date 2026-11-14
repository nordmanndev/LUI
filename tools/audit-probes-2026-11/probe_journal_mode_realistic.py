#!/usr/bin/env python3
"""
Probe: was kostet es, wenn LUI den Journal-Modus nicht mehr selbst setzt?

Der Lesepfad haelt eine Momentaufnahme (Transaktion) fuer Kandidaten und Treffer
("one snapshot for the candidates and the hits", QueryRunner.Execute).

Im WAL-Modus stoeren sich Leser und Schreiber nicht. Im Rollback-Journal haelt der Leser
eine SHARED-Sperre, bis die Transaktion endet: der Schreiber wartet dann so lange wie die
Abfrage dauert (bis zum busy_timeout, dann SQLITE_BUSY).

Gemessen: der Leser haelt die Momentaufnahme 2 ms (die Dauer einer echten Abfrage),
der Schreiber schreibt daneben. busy_timeout 1000 ms.
"""

import os
import sqlite3
import threading
import time

HOLD_MS = 2.0

WRITER = """
import sqlite3, time
con = sqlite3.connect({path!r}, timeout=1.0)
con.execute("PRAGMA busy_timeout = 1000")
started = time.perf_counter()
try:
    con.execute("INSERT INTO t (name) VALUES ('writer')")
    con.commit()
    print(f"    der Schreiber wartete {{(time.perf_counter() - started) * 1000:.2f}} ms und kam durch")
except Exception as exception:
    print(f"    der Schreiber wartete {{(time.perf_counter() - started) * 1000:.2f}} ms und scheiterte: {{exception}}")
"""


def run(journal):
    path = f"/tmp/lui_journal2_{journal}.db"
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(path + suffix):
            os.remove(path + suffix)

    con = sqlite3.connect(path)
    con.execute(f"PRAGMA journal_mode = {journal};")
    con.execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);")
    con.execute("INSERT INTO t (name) VALUES ('reader')")
    con.commit()

    reader = sqlite3.connect(path, timeout=1.0)
    reader.execute("PRAGMA busy_timeout = 1000")
    reader.execute("BEGIN")
    reader.execute("SELECT count(*) FROM t").fetchone()

    print(f"  journal_mode = {journal} (der Leser haelt die Momentaufnahme {HOLD_MS} ms)")
    writer = threading.Thread(target=lambda: exec(WRITER.format(path=path)))
    writer.start()
    time.sleep(HOLD_MS / 1000)
    reader.execute("COMMIT")
    reader.close()
    writer.join()

    con.close()
    for suffix in ("", "-wal", "-shm"):
        if os.path.exists(path + suffix):
            os.remove(path + suffix)


run("WAL")
run("DELETE")
