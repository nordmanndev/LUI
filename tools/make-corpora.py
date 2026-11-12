#!/usr/bin/env python3
"""make-corpora.py — baut die vier Korpora des Repositorys (Issue #123, ADR-0059).

Die Korpora sind **Dateien**, keine Generatoren: eine Liste, die sich mit dem
Skalierungsparameter ändert, ist ein anderes Korpus (Plan vom 2026-11-11, §2).
Jede Datei trägt ihren Kopf (Quelle, Lizenz, Stand, Zahl der Einträge, Format) —
der Name der Datei ist ihre Identität, ein Manifest gibt es nicht.

Drei Regeln, eine für alle vier Dateien:

1. **Der Kopf ist der Block der Kommentarzeilen ganz oben.** Ein `#` in einem
   Eintrag ist **Daten** — ein Filmtitel, der mit `#` beginnt, überlebt.
2. **Die Einträge sind nach dem Hash ihres Textes sortiert** (SHA-256, aufsteigend).
   Ein **Präfix** der Datei ist damit eine repräsentative Stichprobe — nie „die
   ersten N im Alphabet". Das war der Fehler des alten Generators.
3. **Ohne Gewichtsspalte** bekommt der Eintrag ein abgeleitetes Gewicht:
   `1 + floor(9999 · (1 − u)³)`, `u` = die ersten vier Bytes des SHA-256 des
   Textes als Anteil von 2³². Die Orte und die Filme tragen ein **echtes**
   Gewicht (Bevölkerung, Stimmen).

Die Dateien werden gzip-komprimiert eingecheckt (GitHub nimmt keine Datei über
100 MB an, `places.tsv` wiegt über 300 MB). Der Lader liest die komprimierte
Datei mit GZipStream — der Inhalt ist derselbe.

Aufruf:  python3 make-corpora.py <quelle-dir> <ziel-dir> [--snapshot YYYY-MM-DD]
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import os
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

SNAPSHOT_DEFAULT = "2026-11-11"

# Die Zahl der Kopfzeilen je Datei — der zweite Durchgang schreibt nur die ersten
# Zeilen neu, weil ein Eintrag mit `#` Daten ist.
HEADER_LINES: dict[str, int] = {}


def sha256_prefix4(text: str) -> bytes:
    return hashlib.sha256(text.encode("utf-8")).digest()[:4]


def derived_weight(text: str) -> int:
    """1 + floor(9999 · (1 − u)³) — **in exakter Ganzzahlarithmetik**.

    u = v / 2³², v = die ersten vier Bytes des SHA-256 als ganze Zahl ⇒
    (1 − u)³ = t³ / 2⁹⁶ mit t = 2³² − v ⇒ das Gewicht ist
    1 + floor(9999 · t³ / 2⁹⁶) — und das ist in Python und in C# dieselbe Zahl,
    ohne Rundung. Die Regel in Fließkomma-Arithmetik wäre an der Grenze
    zweier Werte um eins daneben.
    """
    v = int.from_bytes(sha256_prefix4(text), "big")
    t = (1 << 32) - v
    return 1 + (9999 * t * t * t) // (1 << 96)


def sort_by_hash(tmp: Path) -> None:
    """Sortiert die Datei `hash<TAB>zeile` nach dem Hash und entfernt die
    Hashspalte. GNU sort arbeitet mit externem Speicher — 5,7 M Zeilen passen
    nicht in den RAM."""
    env = dict(os.environ, LC_ALL="C")
    subprocess.run(["sort", "-S", "256M", "-t", "\t", "-k1,1", "-o", str(tmp), str(tmp)],
                   check=True, env=env)


def write_corpus(dst: Path, name: str, header: list[str], tmp: Path,
                 with_weight: bool, with_context: bool) -> int:
    """Schreibt Kopf + sortierte Einträge in die gzip-Datei und liefert die Zahl
    der Einträge."""
    sort_by_hash(tmp)
    out = dst / f"{name}.gz"
    count = 0
    with gzip.open(out, "wb", compresslevel=9) as gz:
        for line in header:
            gz.write(f"# {line}\n".encode("utf-8"))
        gz.write(b"\n")
        with tmp.open("r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                line = line.rstrip("\n")
                if not line:
                    continue
                # die erste Spalte ist der Hash (der Sortierschlüssel) — sie
                # gehört nicht in die Datei
                _, _, rest = line.partition("\t")
                text, _, tail = rest.partition("\t")
                weight = tail.partition("\t")[0] if tail else ""
                context = tail.partition("\t")[2] if tail else ""
                if not with_weight:
                    weight = str(derived_weight(text))
                parts = [text, weight]
                if with_context:
                    parts.append(context)
                gz.write(("\t".join(parts) + "\n").encode("utf-8"))
                count += 1
    tmp.unlink(missing_ok=True)
    HEADER_LINES[name] = len(header)
    return count


def header_lines(title: str, fmt: str, source: str, licence: str, snapshot: str,
                 kind: str, count: int | None = None, note: str = "") -> list[str]:
    lines = [
        f"LUI corpus — {title}",
        f"format: {fmt}",
        f"kind: {kind}",
        f"source: {source}",
        f"licence: {licence}",
        f"snapshot: {snapshot}",
        "order: the entries are sorted by the SHA-256 of their text — a prefix of this "
        "file is a representative sample, never the alphabetically first N",
    ]
    if note:
        lines.append(f"note: {note}")
    if count is not None:
        lines.insert(5, f"entries: {count}")
    return lines


# Die Quellen der Korpora. Sie werden geholt, wenn sie nicht da sind — das
# Skript laeuft ohne fremde Werkzeuge, und eine Korpusdatei, die niemand bauen
# kann, ist eine, die niemand pruefen kann.
SOURCES = {
    "german.txt":
        "https://raw.githubusercontent.com/kkrypt0nn/wordlists/master/wordlists/languages/german.txt",
    "allCountries.zip": "https://download.geonames.org/export/dump/allCountries.zip",
    "countryInfo.txt": "https://download.geonames.org/export/dump/countryInfo.txt",
    "admin1CodesASCII.txt": "https://download.geonames.org/export/dump/admin1CodesASCII.txt",
    "admin2Codes.txt": "https://download.geonames.org/export/dump/admin2Codes.txt",
    "title.basics.tsv.gz": "https://datasets.imdbws.com/title.basics.tsv.gz",
    "title.ratings.tsv.gz": "https://datasets.imdbws.com/title.ratings.tsv.gz",
    "dewiki-latest-all-titles.gz":
        "https://dumps.wikimedia.org/dewiki/latest/dewiki-latest-all-titles.gz",
}


def fetch(src: Path) -> None:
    """Holt die Quellen, die fehlen."""
    import urllib.request

    src.mkdir(parents=True, exist_ok=True)
    for name, url in SOURCES.items():
        target = src / name
        if target.exists() and target.stat().st_size > 0:
            print(f"{name}: da")
            continue
        print(f"{name}: hole {url}")
        with urllib.request.urlopen(url) as response, target.open("wb") as out:
            while chunk := response.read(1 << 20):
                out.write(chunk)
        print(f"{name}: {target.stat().st_size / 1e6:.1f} MB")


# Das Format jedes Korpus: die **Spalten** und die Bedeutung des Gewichts. Der
# Kopf nennt sie, und der Leser **prueft** sie gegen die Spalten, die er findet —
# eine Selbstbeschreibung, die niemand liest, ist eine zweite Wahrheit.
FORMATS = {
    "words.txt": ("text<TAB>weight", "the weight is the derived weight of the text"),
    "places.tsv": ("text<TAB>weight<TAB>context",
                   "the weight is the population, the context is country/region/district"),
    "movies.tsv": ("text<TAB>weight", "the weight is the number of votes"),
    "wiki-titles.tsv": ("text<TAB>weight", "the weight is the derived weight of the text"),
}


def build_words(src: Path, dst: Path, snapshot: str) -> int:
    words = sorted({w.strip() for w in (src / "german.txt").read_text(
        encoding="utf-8", errors="replace").splitlines() if w.strip()})
    tmp = Path(tempfile.mkstemp(dir="/tmp", prefix="corpus-words-")[1])
    with tmp.open("w", encoding="utf-8") as fh:
        for w in words:
            fh.write(f"{sha256_prefix4(w).hex()}\t{w}\n")
    head = header_lines(
        "the German words",
        "text<TAB>weight",
        "https://github.com/kkrypt0nn/wordlists (wordlists/languages/german.txt)",
        "none stated by the repository for the lists it gathers itself — accepted by the owner",
        snapshot,
        "single",
        note="the weight is the derived weight of the text",
    )
    count = write_corpus(dst, "words.txt", head, tmp, with_weight=False, with_context=False)
    print(f"words.txt: {count} entries")
    return count


def build_places(src: Path, dst: Path, snapshot: str) -> int:
    laender: dict[str, str] = {}
    regionen: dict[str, str] = {}
    bezieke: dict[str, str] = {}
    for line in (src / "countryInfo.txt").read_text(encoding="utf-8").splitlines():
        f = line.split("\t")
        if len(f) > 4:
            laender[f[0]] = f[4]
    for line in (src / "admin1CodesASCII.txt").read_text(encoding="utf-8").splitlines():
        f = line.split("\t")
        if len(f) > 1:
            regionen[f[0]] = f[1]
    for line in (src / "admin2Codes.txt").read_text(encoding="utf-8").splitlines():
        f = line.split("\t")
        if len(f) > 1:
            bezieke[f[0]] = f[1]

    tmp = Path(tempfile.mkstemp(dir="/tmp", prefix="corpus-places-")[1])
    count = 0
    with zipfile.ZipFile(src / "allCountries.zip") as zf, tmp.open("w", encoding="utf-8") as fh:
        with zf.open("allCountries.txt") as raw:
            for bline in io.TextIOWrapper(raw, encoding="utf-8", errors="replace"):
                f = bline.rstrip("\n").split("\t")
                if len(f) < 15 or f[6] not in ("P", "A"):
                    continue
                name = f[1]
                population = f[14] if f[14].isdigit() else "0"
                # die Schluessel der Dateien sind "LAND.ADM1" und "LAND.ADM1.ADM2",
                # in allCountries stehen aber nur "ADM1" und "ADM2" — ohne das
                # Land findet dercontext nichts
                region = regionen.get(f"{f[8]}.{f[10]}", "") if f[10] else ""
                bezirk = bezieke.get(f"{f[8]}.{f[10]}.{f[11]}", "") if f[10] and f[11] else ""
                context = "/".join(x for x in (laender.get(f[8], ""), region, bezirk) if x)
                fh.write(f"{sha256_prefix4(name).hex()}\t{name}\t{population}\t{context}\n")
                count += 1
    head = header_lines(
        "the places of the world",
        "text<TAB>weight<TAB>context",
        "https://download.geonames.org/export/dump/ (allCountries.txt, countryInfo.txt, "
        "admin1CodesASCII.txt, admin2Codes.txt)",
        "CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/) — Geonames",
        snapshot,
        "multi",
        note="the weight is the population, the context is country/region/district",
    )
    count = write_corpus(dst, "places.tsv", head, tmp, with_weight=True, with_context=True)
    print(f"places.tsv: {count} entries")
    return count


def build_movies(src: Path, dst: Path, snapshot: str) -> int:
    stimmen: dict[str, str] = {}
    with gzip.open(src / "title.ratings.tsv.gz", "rt", encoding="utf-8") as fh:
        next(fh)
        for line in fh:
            f = line.rstrip("\n").split("\t")
            if len(f) >= 3:
                stimmen[f[0]] = f[2]

    tmp = Path(tempfile.mkstemp(dir="/tmp", prefix="corpus-movies-")[1])
    seen: set[str] = set()
    count = 0
    with gzip.open(src / "title.basics.tsv.gz", "rt", encoding="utf-8") as fh, \
            tmp.open("w", encoding="utf-8") as out:
        next(fh)
        for line in fh:
            f = line.rstrip("\n").split("\t")
            if len(f) < 3 or f[1] != "movie":
                continue
            title = f[2]
            if title in seen:
                continue
            seen.add(title)
            votes = stimmen.get(f[0], "0") or "0"
            out.write(f"{sha256_prefix4(title).hex()}\t{title}\t{votes}\n")
            count += 1
    head = header_lines(
        "the movie titles",
        "text<TAB>weight",
        "https://datasets.imdbws.com/ (title.basics.tsv, title.ratings.tsv)",
        "the non-commercial IMDb datasets (https://datasets.imdbws.com/#help)",
        snapshot,
        "multi",
        note="the weight is the number of votes",
    )
    count = write_corpus(dst, "movies.tsv", head, tmp, with_weight=True, with_context=False)
    print(f"movies.tsv: {count} entries")
    return count


def build_wiki(src: Path, dst: Path, snapshot: str) -> int:
    tmp = Path(tempfile.mkstemp(dir="/tmp", prefix="corpus-wiki-")[1])
    seen: set[str] = set()
    count = 0
    with gzip.open(src / "dewiki-latest-all-titles.gz", "rt", encoding="utf-8") as fh, \
            tmp.open("w", encoding="utf-8") as out:
        next(fh)
        for line in fh:
            f = line.rstrip("\n").split("\t")
            if len(f) < 2 or f[0] != "0":
                continue
            title = f[1]
            if title in seen:
                continue
            seen.add(title)
            out.write(f"{sha256_prefix4(title).hex()}\t{title}\n")
            count += 1
    head = header_lines(
        "the titles of the German Wikipedia",
        "text<TAB>weight",
        "https://dumps.wikimedia.org/dewiki/latest/dewiki-latest-all-titles.gz",
        "CC BY-SA 4.0 + GFDL (https://creativecommons.org/licenses/by-sa/4.0/)",
        snapshot,
        "multi",
        note="the weight is the derived weight of the text",
    )
    count = write_corpus(dst, "wiki-titles.tsv", head, tmp, with_weight=False, with_context=False)
    print(f"wiki-titles.tsv: {count} entries")
    return count


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("destination")
    ap.add_argument("--snapshot", default=SNAPSHOT_DEFAULT)
    ap.add_argument("--only", action="append", default=None,
                    help="nur dieses Korpus bauen (mehrfach nennbar) — fuer die Reparatur")
    ap.add_argument("--header-only", action="store_true",
                    help="nur die Köpfe der vorhandenen Dateien neu schreiben (die Zahl kommt aus der Datei)")
    args = ap.parse_args()
    src = Path(args.source)
    dst = Path(args.destination)
    dst.mkdir(parents=True, exist_ok=True)

    if not args.header_only:
        fetch(src)

    only = set(args.only or [])
    counts = {
        "words.txt": build_words(src, dst, args.snapshot) if not only or "words.txt" in only else None,
        "places.tsv": build_places(src, dst, args.snapshot) if not only or "places.tsv" in only else None,
        "movies.tsv": build_movies(src, dst, args.snapshot) if not only or "movies.tsv" in only else None,
        "wiki-titles.tsv": build_wiki(src, dst, args.snapshot) if not only or "wiki-titles.tsv" in only else None,
    }
    counts = {name: count for name, count in counts.items() if count is not None or only}
    if args.header_only:
        # der Reparaturmodus: die Dateien bleiben, nur ihre Köpfe werden neu
        # geschrieben — mit der Zahl, die in der Datei steht.
        counts = {name: None for name in counts}

    # Die Zahl der Einträge gehört in den Kopf — also ein zweiter Durchgang:
    # die Datei wird mit dem Kopf neu geschrieben. **Die Zahl ist die Zahl der
    # Zeilen der Datei selbst** — gezählt beim Strömen des Rumpfes, nicht
    # mitgezählt und weitergetragen: eine Zahl im Kopf, die nicht die Zahl der
    # Datei ist, ist eine Lüge. Und der Kopf ist der Block der Kommentarzeilen
    # bis zur Leerzeile — nicht das, was mit `#` beginnt: ein Eintrag, der mit
    # `#` beginnt, ist **Daten**.
    print("\n--- die Köpfe mit der Zahl der Einträge schreiben ---")
    for name in counts:
        path = dst / f"{name}.gz"
        snapshot = args.snapshot
        body = Path(tempfile.mkstemp(dir="/tmp", prefix="corpus-body-")[1])
        head = []
        count = 0
        with gzip.open(path, "rt", encoding="utf-8") as fh, body.open("w", encoding="utf-8") as out:
            # der Kopf ist der Block der Kommentarzeilen bis zur Leerzeile
            while True:
                line = fh.readline()
                if not line:
                    raise SystemExit(f"{path}: der Kopf endet ohne Leerzeile")
                if line.strip() == "":
                    # das Ende des Kopfs — die leere Zeile gehoert zu keinem Eintrag
                    break
                if not line.startswith("#"):
                    raise SystemExit(f"{path}: der Kopf ist kein Block von Kommentaren: {line!r}")
                head.append(line[2:].rstrip("\n"))
            # der Rumpf strömt — 5,8 M Zeilen passen nicht in den Speicher
            for line in fh:
                out.write(line)
                count += 1
        # der Kopf nennt die Zahl der Eintraege der Datei und das Datum des
        # Abrufs der Quelle — beides aus der Datei selbst bzw. aus dem Aufruf,
        # nie aus einer Zahl, die mitgetragen wird.
        head = [h for h in head if not h.startswith("entries:") and not h.startswith("snapshot:")
                and not h.startswith("format:") and not h.startswith("note:")]
        head.insert(1, f"format: {FORMATS[name][0]}")
        head.insert(5, f"entries: {count}")
        head.insert(6, f"snapshot: {snapshot}")
        head.append(f"note: {FORMATS[name][1]}")
        with gzip.open(path, "wb", compresslevel=9) as gz:
            for line in head:
                gz.write(f"# {line}\n".encode("utf-8"))
            gz.write(b"\n")
            with body.open("rb") as fh:
                while chunk := fh.read(1 << 20):
                    gz.write(chunk)
        body.unlink(missing_ok=True)
        print(f"{name}.gz: {count} entries, {path.stat().st_size / 1e6:.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
