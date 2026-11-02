# LUI — Spezifikation

| | |
|---|---|
| **Produkt** | LUI — Autocomplete-Bibliothek für .NET auf SQLite |
| **Status** | Entwurf 2, zur Prüfung (neu: Priorität, § 3 E10, § 9.2, § 12) |
| **Stand** | 2026-11-01 |
| **Zielplattform** | .NET 10, SQLite ≥ 3.45 (über `Microsoft.Data.Sqlite`) |
| **Grundlage** | Messungen vom 2026-11-01 auf 3 Mio. Einträgen (Anhang A) |

> Diese Spezifikation beschreibt **die neue LUI**. Das bisherige Repository
> (Roaring-Bitmaps, eigenes Snapshot-Format) wird umbenannt; aus ihm werden
> nur einzelne Teile übernommen (§ 17).

---

## Inhalt

1. [Zweck und Abgrenzung](#1-zweck-und-abgrenzung)
2. [Begriffe](#2-begriffe)
3. [Grundentscheidungen](#3-grundentscheidungen)
4. [Arten von Indizes](#4-arten-von-indizes)
5. [Inhaltsquellen: eigenständig und verknüpft](#5-inhaltsquellen-eigenständig-und-verknüpft)
6. [Normalisierung](#6-normalisierung)
7. [Datenmodell](#7-datenmodell)
8. [Ablauf einer Abfrage](#8-ablauf-einer-abfrage)
9. [Ranking](#9-ranking)
10. [Tippfehler-Toleranz](#10-tippfehler-toleranz)
11. [Ähnlichkeit (austauschbar)](#11-ähnlichkeit-austauschbar)
12. [Öffentliche API](#12-öffentliche-api)
13. [Synchron, nicht asynchron](#13-synchron-nicht-asynchron)
14. [Konsistenz und Integrität](#14-konsistenz-und-integrität)
15. [Nebenläufigkeit](#15-nebenläufigkeit)
16. [Leistungsziele](#16-leistungsziele)
17. [Pakete, Namespaces, Abhängigkeiten](#17-pakete-namespaces-abhängigkeiten)
18. [Grenzen und Nicht-Ziele](#18-grenzen-und-nicht-ziele)
19. [Teststrategie](#19-teststrategie)
20. [Umsetzungsplan](#20-umsetzungsplan)
21. [Offene Fragen](#21-offene-fragen)
- [Anhang A: Messgrundlage](#anhang-a-messgrundlage)
- [Anhang B: Erwartetes Verhalten aus Nutzersicht](#anhang-b-erwartetes-verhalten-aus-nutzersicht)

---

## 1. Zweck und Abgrenzung

**LUI liefert die Vorschläge eines Autocomplete:** Ein Nutzer tippt in ein
Suchfeld, und LUI antwortet mit den besten 5–20 Einträgen aus einem Bestand
von bis zu einigen Millionen kurzen Texten — Tags, Wörter, Namen,
Rezept- und Buchtitel.

LUI ist eine **Bibliothek**, kein Server. Sie arbeitet in der SQLite-Datenbank
der Anwendung (oder in einer eigenen Datei) und nutzt **SQLite FTS5** als
Suchmaschine.

**LUI ist:**

- für **kurze Texte** gebaut (1–10 Wörter);
- für **viel Lesen, wenig Schreiben** gebaut (typisch: 98 % Abfragen);
- auf **den Anfang von Wörtern** ausgerichtet: Was der Nutzer tippt, ist meist
  der Anfang dessen, was er sucht;
- tolerant gegenüber **Tippfehlern**, **Umlauten** und **Wortreihenfolge**.

**LUI ist nicht:**

- eine Volltextsuche über lange Dokumente (kein BM25, kein Stemming, keine
  Phrasensuche, keine Facetten) — dafür gibt es Lucene, SeekStorm, FTS5 direkt;
- ein Schlüssel-Wert-Speicher;
- ein Server mit eigener HTTP-Schnittstelle.

---

## 2. Begriffe

| Begriff | Bedeutung |
|---|---|
| **Katalog** (`LuiCatalog`) | Der Einstiegspunkt. Kennt den Connection String und alle Indizes einer Datenbank. |
| **Index** | Ein durchsuchbarer Bestand mit eigenem Namen, z. B. `recipes`, `tags`. Hat genau eine **Art** und genau eine **Inhaltsquelle**. |
| **Eintrag** (`Entry`) | Ein durchsuchbares Element: **Schlüssel**, **Text**, **Gewicht**, **Priorität**, optional **Payload**. |
| **Schlüssel** (`Key`) | Die Identität des Eintrags aus Sicht der Anwendung, z. B. die Rezept-Id. |
| **Text** | Was durchsucht und angezeigt wird, z. B. „Gedeckter Apfelkuchen". |
| **Gewicht** | Die Beliebtheit, `0 … 536.870.911`. Bestimmt, **welche** Einträge Kandidaten werden, und entscheidet bei Gleichstand im Ranking. |
| **Priorität** (`Priority`) | Eine Klasse `0 … 3` vor dem Gewicht: `System` (0), `High` (1), `Elevated` (2), `Normal` (3, Standard). Einträge höherer Priorität sind bei jeder passenden Eingabe **zuerst** Kandidaten und stehen im Ranking vorn — z. B. reservierte Systemtags wie „vegan". |
| **Payload** | Daten, die mit dem Treffer zurückkommen (z. B. Slug, Farbe, Icon). Werden **nur für die Treffer** gelesen, nie für alle Kandidaten. |
| **Art** (`IndexKind`) | `SingleWord` oder `MultiWord` (§ 4). Steuert, wie gesucht wird. |
| **Inhaltsquelle** | `Owned` (eigene Tabelle der Bibliothek) oder `Linked` (Tabelle der Anwendung) (§ 5). |
| **Kandidat** | Ein Eintrag, den die Suche vorschlägt, bevor sortiert wird. Höchstens `Candidates` (Standard 500). |
| **Treffer** | Ein Kandidat, der nach dem Ranking unter den ersten `MaxResults` (Standard 20) steht. |
| **Wortanfang** | Die Eingabe steht am Anfang eines Wortes des Eintrags (§ 9.1). |

---

## 3. Grundentscheidungen

Jede Entscheidung bekommt in der Umsetzung ein ADR. Die Begründungen stehen
hier in Kurzform, die Zahlen in Anhang A.

| # | Entscheidung | Begründung |
|---|---|---|
| E1 | **SQLite FTS5 mit Trigram-Tokenizer** ist die Suchmaschine. | Findet Teilstrings überall im Wort („kuch" in „Apfelkuchen"), steckt in `Microsoft.Data.Sqlite` bereits drin, Backup und Transaktionen der Anwendung gelten mit. |
| E2 | **Priorität und Gewicht stecken in der `rowid`** des FTS-Index, das Gewicht umgekehrt kodiert: `rowid = (priority << 61) \| ((2²⁹−1 − weight) << 32) \| docId`. Abfragen laufen mit `ORDER BY rowid LIMIT n`. | FTS5 kann nur nach `rowid` oder `rank` sortieren; eine Spalte `weight` zwingt SQLite, **alle** Treffer zu lesen (231.452 Treffer: 194 ms). Mit der `rowid` liest FTS5 nur die ersten n, **vorwärts** (0,6–3 ms, identische Top 500). `DESC` wäre bei seltenen Abfragen ~60 % langsamer. |
| E3 | **External content mit Triggern** ist der einzige Index-Mechanismus. Inhaltsquelle ist entweder eine eigene Tabelle der Bibliothek oder eine Tabelle der Anwendung (§ 5). | SQLite hält Index und Inhaltstabelle in **derselben Transaktion** konsistent; `rebuild` und `integrity-check` sind eingebaut. Ein Mechanismus für beide Quellen. |
| E4 | **Normalisierung in eine Richtung**: klein, ä→ae, ö→oe, ü→ue, ß→ss, übrige Akzente entfernen — beim Indexieren und bei der Abfrage gleich (§ 6). | Deutsche Nutzer tippen „mueller" und „müller"; beides wird `mueller`. SQLites `remove_diacritics` allein kann ü→ue und ß→ss nicht. |
| E5 | **Ranking zuerst nach Wortanfang**, dann Ähnlichkeit, dann Gewicht (§ 9). | Reine Ähnlichkeit nach der Kandidatenwahl: 13 % / 27 % Hit@5 / Hit@20; Wortanfang zuerst: 45 % / 61 %. |
| E6 | **Tippfehler über ein SymSpell-Wörterbuch der Wortanfänge** (3–10 Zeichen), 1 Fehler, 3 beste Korrekturen (§ 10). | SymSpell mit ganzen Wörtern korrigiert angefangene Wörter kaum (11 %); mit Wortanfängen 40 %, also gut 80 % der Obergrenze ohne Tippfehler (49 %). |
| E7 | **Payload nur für die Treffer**, in einer Abfrage `WHERE key IN (…)`. | 29 µs für 20 Treffer; 40 % weniger Allokation als Payload für alle 500. |
| E8 | **Synchrone API** (§ 13). | `Microsoft.Data.Sqlite` führt asynchrone Methoden synchron aus; ein `Task` wäre eine Täuschung. |
| E9 | **Standard: die Datenbank der Anwendung**; eine eigene Datei ist über den Connection String möglich. | Nur in derselben Datenbank sind Fachdaten und Index atomar, und nur dort ist `Linked` möglich. |
| E10 | **Priorität statt Boost.** Systemtags u. Ä. bekommen eine Priorität in der `rowid` und sind damit bei jeder passenden Eingabe die ersten Kandidaten. | Ein Boost im Ranking wirkt zu spät: Was nicht unter den 500 Kandidaten ist, wird nicht gerankt. Gemessen: „vegan" (Gewicht 5) bei `gan` (33.402 Treffer) ohne Priorität **nicht** unter den Kandidaten, mit Priorität **Kandidat Nr. 1**; Plan unverändert (Anhang A, A8). |

---

## 4. Arten von Indizes

Die Art wird **beim Anlegen** festgelegt und steuert die Abfrage **intrinsisch**.
Der Aufrufer muss bei der Abfrage nicht wissen, wie gesucht wird.

| | **`SingleWord`** | **`MultiWord`** |
|---|---|---|
| Für | Tags, einzelne Wörter, Komposita, Hashtags | Namen, Rezepttitel, Buchtitel |
| Beispiele | `Apfelkuchen`, `#Bahnhof`, `vegan` | `Anna Müller`, `Gedeckter Apfelkuchen`, `Der Herr der Ringe` |
| Suchspalten | `s_norm` | `s_norm` + `s_compact` (ohne Leerzeichen/Bindestriche) in der Tabelle; im Suchindex nur `s_compact` (ADR-0011) |
| Eingabe | als Ganzes | in Wörter zerlegt, **UND**-verknüpft (im kompakten Text) |
| Wortreihenfolge | — | egal (`kuchen apfel` = `apfel kuchen`) |
| Leerzeichen | — | egal (`apfelkuch` findet „Apfel Kuchen") |
| Standard-Ähnlichkeit (§ 11) | Jaro-Winkler | N-gram Dice |

---

## 5. Inhaltsquellen: eigenständig und verknüpft

Beide Quellen nutzen **denselben** Mechanismus (E3). Sie unterscheiden sich nur
darin, **wessen Tabelle** die Inhalte liefert.

| | **`Owned`** (eigenständig) | **`Linked`** (verknüpft) |
|---|---|---|
| Inhaltstabelle | `lui_<index>` — gehört der Bibliothek | eine Tabelle der Anwendung, z. B. `recipe` |
| Wer schreibt hinein | die Anwendung über `Upsert` / `Remove` / `Sync` | die Anwendung mit normalem SQL (oder ORM) |
| Index ↔ Inhaltstabelle | konsistent durch Trigger | konsistent durch Trigger |
| Inhaltstabelle ↔ Fachdaten | **Verantwortung der Anwendung**; Werkzeuge: `Sync`, `Check` (§ 14) | entfällt — es ist dieselbe Tabelle |
| Änderung am Schema der Anwendung | keine | 5 berechnete Spalten + 3 Indizes + 3 Trigger auf der Tabelle |
| Text doppelt gespeichert | ja | nein |
| Eigene Datei möglich | ja | nein |
| Eigenes Gewicht (z. B. Klicks im Autocomplete) | ja | nur über eine Spalte der Tabelle |
| Filter, was in den Index kommt | ja (die Anwendung entscheidet) | nein (alle Zeilen); optional später per `WHERE` (§ 21, O6) |

**Hinzufügen eines Index** ist in beiden Fällen ein Aufruf; die Bibliothek legt
ihre Tabellen, Spalten, Indizes und Trigger selbst an und vermerkt die
Definition in `lui_meta` (§ 7.1). Indizes sind voneinander unabhängig.

---

## 6. Normalisierung

### 6.1 Regel

Eine Funktion, **beim Indexieren und bei der Abfrage gleich**:

1. Kleinschreibung (invariant).
2. Deutsche Umschrift: `ä→ae`, `ö→oe`, `ü→ue`, `ß→ss` (groß und klein).
3. Übrige Akzente entfernen (Unicode-Zerlegung, Kombinationszeichen verwerfen):
   `é→e`, `ç→c`, `ñ→n`.
4. Für `s_compact` zusätzlich: alles außer Buchstaben und Ziffern entfernen.

| Eingabe | `s_norm` | `s_compact` |
|---|---|---|
| Gedeckter Apfelkuchen mit Äpfeln | gedeckter apfelkuchen mit aepfeln | gedeckterapfelkuchenmitaepfeln |
| Straße | strasse | strasse |
| Café Crème | cafe creme | cafecreme |
| Apfel-Zimt-Kuchen | apfel-zimt-kuchen | apfelzimtkuchen |

Die deutsche Umschrift ist pro Index abschaltbar (`.GermanTransliteration(false)`)
für Bestände ohne deutsche Texte.

### 6.2 Wo die Regel läuft

| Stelle | Umsetzung |
|---|---|
| Suchspalten `s_norm`, `s_compact` | **SQL**, als berechnete Spalten (`replace(…)` für ä/ö/ü/ß, Groß/Klein und übrige Akzente durch den Tokenizer `trigram remove_diacritics 1`) |
| Präfix-Spalten `s_p1`, `s_p2` (§ 7) | **SQL**, wie oben, plus `lower()` für ASCII |
| Eingabe des Nutzers, Wortanfang, Ähnlichkeit, SymSpell | **C#** (`Normalizer`) |

Die Regel existiert damit **zweimal**. Ein Test muss für eine große Stichprobe
(alle 685.829 Wörter der deutschen Wortliste plus Sonderfälle) zeigen, dass
SQL und C# dieselbe Suchform liefern (§ 19).

### 6.3 Bekannte Folge

`muller` (ohne e) findet „Müller" nicht über den Index, weil dort `mueller`
steht. Die Tippfehler-Toleranz fängt das ab: `muller` ist einen Buchstaben von
`mueller` entfernt (§ 10).

---

## 7. Datenmodell

Alle Objekte der Bibliothek tragen das Präfix `lui_`. Alle Bezeichner werden
gegen `^[a-z][a-z0-9_]{0,40}$` geprüft und **nie** ungeprüft in SQL eingesetzt.

### 7.1 Katalog

```sql
CREATE TABLE IF NOT EXISTS lui_meta (
    index_name   TEXT PRIMARY KEY,
    kind         TEXT NOT NULL,          -- SingleWord | MultiWord
    source       TEXT NOT NULL,          -- Owned | Linked
    definition   TEXT NOT NULL,          -- JSON: Tabelle, Spalten, Optionen
    schema_version INTEGER NOT NULL,
    created_at   TEXT NOT NULL           -- ISO-8601, aus TimeProvider
);
```

`Ensure()` legt einen Index an oder prüft, dass die gespeicherte Definition zur
gewünschten passt. Eine Abweichung ist ein Fehler mit klarer Meldung — ein
Index wird nie stillschweigend umgebaut (§ 12.5).

### 7.2 Inhaltstabelle bei `Owned`

```sql
CREATE TABLE lui_recipes (
    doc_id   INTEGER PRIMARY KEY,                 -- intern, ≤ 4.294.967.295
    key      TEXT    NOT NULL UNIQUE,             -- Schlüssel der Anwendung
    text     TEXT    NOT NULL,
    weight   INTEGER NOT NULL CHECK (weight BETWEEN 0 AND 536870911),
    priority INTEGER NOT NULL DEFAULT 3 CHECK (priority BETWEEN 0 AND 3),   -- 0 = System … 3 = Normal
    payload  BLOB,                                -- serialisiert (§ 12.3)
    s_norm    TEXT    GENERATED ALWAYS AS (<Normalisierung von text>) STORED,
    s_compact TEXT    GENERATED ALWAYS AS (<s_norm ohne Leerzeichen/Bindestriche>) STORED,
    s_p1      TEXT    GENERATED ALWAYS AS (substr(<s_norm ohne '#'>, 1, 1)) STORED,
    s_p2      TEXT    GENERATED ALWAYS AS (substr(<s_norm ohne '#'>, 1, 2)) STORED,
    s_rowid   INTEGER GENERATED ALWAYS AS ((priority << 61) | ((536870911 - weight) << 32) | doc_id) STORED
);
CREATE UNIQUE INDEX lui_recipes_rowid ON lui_recipes(s_rowid);
CREATE INDEX lui_recipes_p1 ON lui_recipes(s_p1, s_rowid);   -- s_rowid = Priorität, dann Gewicht absteigend
CREATE INDEX lui_recipes_p2 ON lui_recipes(s_p2, s_rowid);
```

**Aufbau der `rowid`** (64 Bit, immer positiv):

```
[0][ Priorität: 2 Bit ][ 2²⁹−1 − Gewicht: 29 Bit ][ doc_id: 32 Bit ]
```

Kleinere `rowid` = höhere Priorität, bei gleicher Priorität höheres Gewicht.
Die Rundreise (kodieren → dekodieren) ist ein Unit-Test.

`s_compact` entfällt bei `SingleWord`.

### 7.3 Inhaltstabelle bei `Linked`

Die Tabelle der Anwendung bekommt dieselben berechneten Spalten (`s_norm`,
`s_compact`, `s_p1`, `s_p2`, `s_rowid`) und dieselben drei Indizes, abgeleitet
aus den angegebenen Spalten für Text, Gewicht und — optional — Priorität.
Voraussetzungen:

- die Schlüsselspalte ist `INTEGER PRIMARY KEY` (sie dient als `doc_id`,
  Wertebereich 0 … 4.294.967.295);
- die Gewichtsspalte ist `INTEGER NOT NULL` im Wertebereich von § 2;
- die Prioritätsspalte (optional) ist `INTEGER NOT NULL` mit `0 … 3`; fehlt
  sie, gilt für alle Zeilen `Normal`. Ein Flag wie `is_system` lässt sich als
  berechnete Spalte der Anwendung abbilden
  (`GENERATED ALWAYS AS (CASE WHEN is_system THEN 0 ELSE 3 END)`).

Die Bibliothek ändert das Schema der Anwendung **nur** in `Ensure()` und nur
nach ausdrücklicher Erlaubnis (`.LinkedTo(…, allowSchemaChange: true)`).

### 7.4 Suchindex (beide Quellen)

Der Suchindex enthält **eine** Spalte: bei `SingleWord` `s_norm`, bei `MultiWord` `s_compact`
(ADR-0011). Das Beispiel zeigt `MultiWord`; bei `SingleWord` steht überall `s_norm` statt `s_compact`.

```sql
CREATE VIRTUAL TABLE lui_recipes_fts USING fts5(
    s_compact,
    content = 'lui_recipes', content_rowid = 's_rowid',
    tokenize = 'trigram remove_diacritics 1');

CREATE TRIGGER lui_recipes_ai AFTER INSERT ON lui_recipes BEGIN
  INSERT INTO lui_recipes_fts(rowid, s_compact)
  VALUES (new.s_rowid, new.s_compact); END;

CREATE TRIGGER lui_recipes_ad AFTER DELETE ON lui_recipes BEGIN
  INSERT INTO lui_recipes_fts(lui_recipes_fts, rowid, s_compact)
  VALUES ('delete', old.s_rowid, old.s_compact); END;

CREATE TRIGGER lui_recipes_au AFTER UPDATE OF text, weight, priority ON lui_recipes
WHEN old.s_rowid IS NOT new.s_rowid OR old.s_compact IS NOT new.s_compact BEGIN
  INSERT INTO lui_recipes_fts(lui_recipes_fts, rowid, s_compact)
  VALUES ('delete', old.s_rowid, old.s_compact);
  INSERT INTO lui_recipes_fts(rowid, s_compact)
  VALUES (new.s_rowid, new.s_compact); END;
```

Die `WHEN`-Bedingung hält den Suchindex unberührt, wenn eine Änderung den indexierten Text nicht ändert (ein
identisches `Upsert`, eine Änderung der Payload, ein Austausch von Leerzeichen und Bindestrichen).

**Änderung gegenüber Entwurf 2 (ADR-0011):** Der Suchindex enthielt bei `MultiWord` `s_norm` **und** `s_compact`.
`s_compact` enthält alles, was eine Abfrage braucht; `s_norm` bleibt Spalte der Tabelle (Präfix-Spalten, Ranking). Gemessen
an 500.000 Titeln: −36 % Datei, −40 % Aufbau, −30 % Abfragezeit.

### 7.5 Datenbank-Einstellungen

Die Bibliothek setzt beim Öffnen einer Verbindung, wenn nicht anders
konfiguriert: `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout`.
`cache_size` und `mmap_size` sind Optionen des Katalogs; sie bestimmen, ob der
Index im Speicher liegt (Anhang A, A6).

---

## 8. Ablauf einer Abfrage

```
Eingabe ─► Normalisieren ─► Länge < 3? ──ja──► Präfix-Pfad ─────────────┐
                               │ nein                                    │
                               ▼                                         │
                     Kandidaten aus FTS5 (nach Art, § 8.2)               │
                               │                                         │
                    kein Kandidat beginnt an einem Wortanfang?           │
                         │ ja                        │ nein              │
                         ▼                           │                   │
              Tippfehler-Korrektur (§ 10):           │                   │
              3 Korrekturen → Kandidaten vereinigen  │                   │
                         └───────────┬───────────────┘                   │
                                     ▼                                   │
                         Ranking (§ 9) ◄─────────────────────────────────┘
                                     ▼
                Top MaxResults ─► Payload IN (…) ─► Ergebnis
```

### 8.1 Präfix-Pfad (1–2 Zeichen)

Trigramme brauchen drei Zeichen. Für kürzere Eingaben:

```sql
SELECT doc_id, text FROM lui_recipes
WHERE s_p1 = $q            -- bzw. s_p2 bei zwei Zeichen
ORDER BY s_rowid           -- Priorität, dann Gewicht absteigend
LIMIT $candidates;
```

Das findet zuerst die Einträge höherer Priorität, dann die beliebtesten,
deren **Text** so beginnt (nicht jedes Wort; § 21, O1).

### 8.2 Kandidaten aus FTS5 (≥ 3 Zeichen)

| Art | FTS5-Ausdruck für `apfel kuch` bzw. `apfelkuch` |
|---|---|
| `SingleWord` | `s_norm : "apfelkuch"` |
| `MultiWord` | `s_compact : ("apfel" AND "kuch")` |

- Wörter mit weniger als 3 Zeichen gehen nicht in den Ausdruck ein, aber in das
  Ranking. Bleibt kein Wort von mindestens 3 Zeichen, steht die ganze Eingabe kompakt im Ausdruck
  (`s_compact : "abcd"` für `ab cd`).
- Ein Wort ist eine Phrase (`s_compact : "apfelkuch"`). Der Ausdruck der ersten Fassung,
  `(s_norm : ("apfel" AND "kuch")) OR (s_compact : "apfelkuch")`, findet nichts, was der heutige nicht findet;
  der heutige findet zusätzlich Titel, deren Wörter über eine Wortgrenze des Titels laufen (`elk ort` in
  „Apfel Kuchen Torte"). Das Ranking setzt sie hinter jeden Wortanfang (ADR-0011).
- Jeder Begriff wird als FTS5-Zeichenkette in `"…"` gesetzt, Anführungszeichen
  verdoppelt; die Eingabe wird nie roh in den Ausdruck kopiert.

```sql
SELECT c.doc_id, c.text FROM lui_recipes_fts f
JOIN lui_recipes c ON c.s_rowid = f.rowid
WHERE lui_recipes_fts MATCH $expr
ORDER BY f.rowid               -- höchste Priorität, dann höchstes Gewicht zuerst, ohne Sortierer
LIMIT $candidates;
```

Plan: `SCAN … VIRTUAL TABLE INDEX 64` und `SEARCH … USING INDEX …_rowid`.
Ein Test hält den Plan fest (§ 19).

### 8.3 Ergebnis

```csharp
public sealed record LuiResult<TKey, TPayload>(
    string Query,
    IReadOnlyList<LuiHit<TKey, TPayload>> Hits,
    int CandidatesConsidered,     // wie viele bewertet wurden (≤ Candidates)
    bool Truncated,               // es gab mehr als Candidates Kandidaten
    string? CorrectedQuery,       // gesetzt, wenn die Tippfehler-Korrektur gegriffen hat
    LuiTimings? Timings);         // nur mit .TrackTimings()

public readonly record struct LuiHit<TKey, TPayload>(
    TKey Key, string Text, TPayload? Payload, int Weight, Priority Priority,
    double Similarity, bool AtWordStart, Highlight? Highlight);
```

---

## 9. Ranking

### 9.1 Wortanfang

**Definition:** Ein Kandidat erfüllt *Wortanfang*, wenn es ein Wort `i` im
normalisierten Text gibt, sodass der **kompakte Text ab Wort `i`** mit der
**kompakten Eingabe** beginnt. Wörter werden an Leerzeichen und Bindestrichen
getrennt; ein führendes `#` zählt nicht.

| Eingabe | Eintrag | Wortanfang |
|---|---|:-:|
| `apfelkuch` | **Apfelkuch**en | ✅ (Wort 1) |
| `apfelkuch` | Gedeckter **Apfelkuch**en | ✅ (Wort 2) |
| `apfelkuch` | **Apfel Kuch**en | ✅ (kompakt ab Wort 1) |
| `apfel kuch` | **Apfelkuch**en | ✅ (kompakt) |
| `apfelkuch` | Brat**apfelkuch**en | ❌ (mitten im Wort) |
| `kuch` | **Kuch**enblech | ✅ |
| `kuch` | Apfel**kuch**en | ❌ |

### 9.2 Reihenfolge

**Stufe 0 ist die Priorität** (Standard: an). Einträge höherer Priorität, die
die Eingabe enthalten, stehen vor allen anderen; untereinander gelten die
Stufen der gewählten `RankingOrder`. Mit `.Priorities(PriorityRanking.CandidatesOnly)`
sind sie nur **sicher Kandidaten** und werden normal einsortiert.

```csharp
public enum RankingOrder
{
    WordStartThenSimilarity,   // Standard: Wortanfang → Ähnlichkeit → Gewicht
    WordStartThenWeight,       // Wortanfang → Gewicht → Ähnlichkeit
    SimilarityOnly,            // Ähnlichkeit → Gewicht
    WeightOnly,                // Gewicht (= Reihenfolge der Kandidaten)
}

public enum PriorityRanking
{
    First,                     // Standard: Priorität vor allen Stufen
    CandidatesOnly,            // nur sicher unter den Kandidaten, normal einsortiert
}
```

**Doppelte Texte** (z. B. ein Nutzer-Tag „Vegan" neben dem Systemtag „vegan"):
Mit `.DistinctText()` wird pro normalisiertem Text nur der Eintrag mit der
höchsten Priorität (dann dem höchsten Gewicht) gezeigt. Standard: aus.

**Die Ähnlichkeit wird ab dem Wort gemessen, an dem der Treffer beginnt**, nicht
ab dem Anfang des ganzen Textes. „Gedeckter Apfelkuchen" bekommt für
`apfelkuch` so 0,99 statt 0,38. Ohne Wortanfang wird gegen den ganzen Text
gemessen.

Bei Gleichstand auf allen Stufen entscheidet die `rowid` (also Gewicht, dann
`doc_id`); das Ergebnis ist damit deterministisch.

### 9.3 Markierung der Fundstelle (optional)

Weil die Normalisierung die Länge ändert (ü → ue), liefert LUI die Fundstelle
**im Originaltext** zurück (`Highlight(Start, Length)`). Dafür bildet der
`Normalizer` neben der Suchform eine Zuordnung Suchform-Position →
Original-Position. Abschaltbar, weil sie pro Treffer Arbeit kostet.

---

## 10. Tippfehler-Toleranz

### 10.1 Wörterbuch

- **Quelle:** alle Wörter (≥ 3 Zeichen) der normalisierten Texte eines Index.
- **Inhalt:** alle **Wortanfänge** der Länge 3 … 10 dieser Wörter, jeweils mit
  der Summe der Gewichte als Häufigkeit.
- **Werkzeug:** SymSpell (`symspell` 6.7.3, MIT), maximal **1** Fehler.
- **Ort:** im Arbeitsspeicher, pro Index. Aufbau beim ersten Zugriff aus der
  Inhaltstabelle; danach ergänzt `Upsert` die neuen Wortanfänge. `Remove` lässt
  sie stehen (SymSpell kennt kein Entfernen; eine Korrektur dorthin findet
  schlicht nichts). `RebuildTypoDictionary()` baut neu.

### 10.2 Wann korrigiert wird

Wenn **kein Kandidat** die Bedingung *Wortanfang* erfüllt — **nicht** erst bei
0 Treffern: 34 % der Tippfehler haben zufällig Treffer mitten in anderen
Wörtern.

### 10.3 Wie korrigiert wird

1. Jedes Wort der Eingabe mit ≥ 3 Zeichen wird nachgeschlagen
   (`Verbosity.Closest`, Distanz 1).
2. Die **3 besten Korrekturen** werden als Kandidatensuche ausgeführt, die
   Kandidaten vereinigt (je `doc_id` der beste Wert).
3. Das Ranking läuft mit der korrigierten Eingabe; `CorrectedQuery` sagt, was
   gesucht wurde.

### 10.4 Grenzen

Bei 4–5 Zeichen ist auch ohne Tippfehler nur begrenzt etwas zu holen (viele
Wörter beginnen gleich). Ab 6 Zeichen erreicht die Korrektur 86–92 % der
Trefferquote ohne Tippfehler.

---

## 11. Ähnlichkeit (austauschbar)

```csharp
public interface ITextSimilarity
{
    string Name { get; }
    double Similarity(string query, string text);     // 0 … 1, beide normalisiert
}
```

| Implementierung | Standard für | pro Vergleich (15 vs. 38 Zeichen) |
|---|---|---:|
| `Similarity.JaroWinkler` | `SingleWord` | 4,5 µs, 336 B |
| `Similarity.NgramDice` | `MultiWord` | 12,2 µs, 6,5 KB |
| `Similarity.DamerauLevenshtein` | Codes, Nummern | 13,3 µs, 3,6 KB |
| `Similarity.Levenshtein` | — | 6,4 µs, 352 B |
| eigene | — | — |

Implementiert über `F23.StringSimilarity` (MIT). Die Ähnlichkeit wird **nur**
für die Kandidaten berechnet (≤ `Candidates`) und nur in Stufe 2 des Rankings
(§ 9.2).

---

## 12. Öffentliche API

### 12.1 Katalog öffnen

```csharp
using Nordmann.LUI;

using var lui = LuiCatalog.Open("Data Source=app.db", options => options
    .CacheSize(megabytes: 256)
    .MemoryMap(megabytes: 1024)
    .UseTimeProvider(TimeProvider.System));
```

### 12.2 Index anlegen (fluent)

```csharp
// eigenständig: beliebige Quelle, eigene Inhaltstabelle
ILuiIndex<long, RecipeChip> recipes = lui
    .Index<long, RecipeChip>("recipes")
    .MultiWord()
    .Owned()
    .Payload(RecipeJsonContext.Default.RecipeChip)       // System.Text.Json, quellgeneriert (AOT)
    .Similarity(Similarity.NgramDice)                    // Standard für MultiWord, hier explizit
    .TypoTolerance(maxEdits: 1)
    .Ensure();

// verknüpft: hängt sich an eine Tabelle der Anwendung
ILuiIndex<long, TagChip> tags = lui
    .Index<long, TagChip>("tags")
    .SingleWord()
    .LinkedTo(table: "tag", key: "id", text: "name", weight: "usage_count", priority: "priority", allowSchemaChange: true)
    .Payload(reader => new TagChip(reader.GetString("slug"), reader.GetString("color")))   // aus Spalten der Tabelle
    .Ensure();
```

Regeln:

- Jede Methode des Builders gibt den Builder zurück; `Ensure()` schließt ab.
- `Ensure()` ist **idempotent**: Existiert der Index mit gleicher Definition,
  wird er geöffnet; weicht die Definition ab, wird mit einer Meldung abgebrochen,
  die beide Definitionen nennt.
- `Drop()` entfernt einen Index samt Tabellen, Spalten, Indizes und Triggern.

### 12.3 Schlüssel und Payload

- `TKey` ist `long` oder `string`. Bei `Linked` ist er `long` (Primärschlüssel).
- **`Owned`**: Die Payload wird serialisiert gespeichert. Die API nimmt eine
  `JsonTypeInfo<TPayload>` (AOT- und trimmsicher) oder einen eigenen
  `IPayloadSerializer<TPayload>`.
- **`Linked`**: Die Payload wird aus Spalten der Tabelle gelesen; die API nimmt
  eine Abbildung `Func<LuiRow, TPayload>` und die Liste der benötigten Spalten.
- In beiden Fällen: Payload **nur für die Treffer**, eine Abfrage
  `WHERE key IN (…)` bzw. `WHERE id IN (…)`.

### 12.4 Schreiben (`Owned`)

```csharp
recipes.Upsert(42, "Gedeckter Apfelkuchen", weight: 9000, new RecipeChip("gedeckter-apfelkuchen"));
recipes.UpdateWeight(42, 9500);
recipes.Remove(42);

// reservierter Systemtag: immer zuerst, wenn er zur Eingabe passt
tagIndex.Upsert(1, "vegan", weight: 0, chip, priority: Priority.System);
tagIndex.UpdatePriority(1, Priority.Normal);

// in der Transaktion der Anwendung — Fachdaten und Index ändern sich atomar
using var tx = connection.BeginTransaction();
// … eigenes INSERT …
recipes.Upsert(42, "Gedeckter Apfelkuchen", 9000, chip, transaction: tx);
tx.Commit();

// Abgleich mit einer Quelle: ergänzt, ändert, entfernt, was fehlt
SyncReport report = recipes.Sync(source, options => options.BatchSize(10_000).Progress(progress), cancellationToken);
```

Bei `Linked` gibt es diese Methoden nicht: Die Anwendung schreibt in ihre
Tabelle, die Trigger erledigen den Rest. `UpdateWeight` ist dort ein `UPDATE`
der Gewichtsspalte.

### 12.5 Abfragen (fluent)

```csharp
LuiResult<long, RecipeChip> result = recipes
    .Query("apfelkuch")
    .Take(20)                                        // MaxResults, Standard 20
    .Candidates(500)                                 // Standard 500
    .Rank(RankingOrder.WordStartThenSimilarity)      // Standard
    .Priorities(PriorityRanking.First)               // Standard
    .DistinctText(false)                             // Standard
    .Similarity(Similarity.JaroWinkler)              // überschreibt den Standard der Art
    .TypoTolerance(true)                             // Standard: wie beim Index festgelegt
    .Highlight(true)                                 // Standard: false
    .TrackTimings(false)
    .Execute(cancellationToken);

// kurz:
var hits = recipes.Query("apfelkuch").Execute().Hits;

// ohne Builder, für Konfiguration aus Dateien:
var same = recipes.Query("apfelkuch", new LuiQueryOptions { MaxResults = 20 });
```

Was die Abfrage **nicht** als Option hat, weil es aus der Art folgt: wie gesucht
wird (ganz / in Wörtern / kompakt), der Präfix-Pfad, die Regel *Wortanfang*.

### 12.6 Prüfen und reparieren

```csharp
IntegrityReport check = recipes.Check(source);   // Owned: Abweichungen zur Quelle (fehlt / überzählig / anders)
IntegrityReport fts   = recipes.Check();         // beide: FTS5 integrity-check, Trigger vorhanden, Definition = lui_meta
recipes.Rebuild();                               // FTS5 'rebuild' aus der Inhaltstabelle
recipes.Optimize();                              // FTS5 'optimize'
```

### 12.7 Dependency Injection

```csharp
services.AddLui("Data Source=app.db")
        .AddIndex<long, RecipeChip>("recipes", index => index.MultiWord().Owned().Payload(RecipeJsonContext.Default.RecipeChip))
        .AddIndex<long, TagChip>("tags", index => index.SingleWord().LinkedTo("tag", "id", "name", "usage_count", allowSchemaChange: true));

// Verwendung: Keyed Services
public sealed class SuggestEndpoint([FromKeyedServices("recipes")] ILuiIndex<long, RecipeChip> recipes) { … }
```

`Ensure()` läuft beim Start (als `IHostedService`, abschaltbar), nicht beim
ersten Request.

### 12.8 Fehler

| Situation | Ausnahme |
|---|---|
| ungültiger Index- oder Spaltenname | `ArgumentException` mit dem erlaubten Muster |
| Definition weicht von `lui_meta` ab | `LuiDefinitionMismatchException` (nennt beide) |
| `Linked` ohne `allowSchemaChange` und fehlende Spalten | `LuiSchemaChangeRequiredException` (nennt das nötige SQL) |
| Gewicht außerhalb des Bereichs | `ArgumentOutOfRangeException` |
| SQLite meldet `SQLITE_BUSY` nach `busy_timeout` | `SqliteException` wird unverändert weitergereicht |

---

## 13. Synchron, nicht asynchron

**Die API ist synchron.** `Microsoft.Data.Sqlite` unterstützt kein
asynchrones I/O; seine `…Async`-Methoden laufen synchron, und Microsoft rät,
sie zu meiden. Eine `Task`-API würde Asynchronität vortäuschen, die es nicht
gibt — und `Task.Run` als Hülle verlagert die Arbeit nur auf einen anderen
Thread des Pools.

**Folgen:**

- Abfragen sind kurz (≈ 1–3 ms) und CPU-gebunden; in ASP.NET Core werden sie
  direkt im Request ausgeführt.
- Lange Vorgänge (`Sync`, `Rebuild`, erster Aufbau) nehmen ein
  `CancellationToken` und ein `IProgress<…>` und sind für einen Hintergrunddienst
  gedacht.
- **Eine Ausnahme:** `Sync` hat eine Überladung für `IAsyncEnumerable<Entry>`,
  weil die **Quelle** asynchron sein kann (z. B. EF Core gegen eine andere
  Datenbank). Die Schreibvorgänge nach SQLite bleiben synchron.

---

## 14. Konsistenz und Integrität

| Ebene | `Owned` | `Linked` |
|---|---|---|
| Suchindex ↔ Inhaltstabelle | Trigger, dieselbe Transaktion | Trigger, dieselbe Transaktion |
| Inhaltstabelle ↔ Fachdaten | Anwendung: `Upsert` in ihrer Transaktion, sonst `Sync` | entfällt |
| Prüfung | `Check(source)`, `Check()` | `Check()` |
| Reparatur | `Sync(source)`, `Rebuild()` | `Rebuild()` |

`Sync` arbeitet in Stapeln (Standard 10.000) in je einer Transaktion und
markiert gesehene Schlüssel; am Ende werden nicht gesehene entfernt. Er ist
abbrechbar und wieder aufsetzbar.

---

## 15. Nebenläufigkeit

- **Lesen** ist parallel: Jede Abfrage nutzt eine eigene Verbindung aus dem Pool
  von `Microsoft.Data.Sqlite` (WAL erlaubt viele Leser neben einem Schreiber).
- **Schreiben** ist in SQLite seriell (ein Schreiber pro Datei). Bei 20
  Änderungen am Tag ist das bedeutungslos; ein großer `Sync` oder `Rebuild`
  blockiert aber die Schreibvorgänge der Anwendung in derselben Datei, solange
  eine Stapel-Transaktion offen ist.
- **SymSpell-Wörterbuch:** Lesen ohne Sperre; Ergänzungen unter einer Sperre
  pro Index (SymSpell ist für gleichzeitiges Schreiben und Lesen nicht
  ausgelegt). Ein Test mit parallelen Abfragen und gleichzeitigem `Upsert`
  sichert das ab.
- **Lebensdauer:** `LuiCatalog` und alle `ILuiIndex` sind Singletons;
  `Dispose()` des Katalogs wartet, bis laufende Abfragen fertig sind
  (Zähler pro Abfrage), und gibt dann frei.

---

## 16. Leistungsziele

Abgeleitet aus Anhang A (4-Kern-VM, Datenbank im Page Cache). Sie werden als
Tests festgehalten (§ 19) und mit Kommando in `docs/benchmarks.md`
veröffentlicht.

| Messung | Szenario | Ziel |
|---|---|---|
| Abfrage ohne Korrektur | 3 Mio. Einträge, Eingaben 1–8 Zeichen | p50 ≤ 2 ms, p95 ≤ 5 ms |
| Abfrage mit Tippfehler-Korrektur | dasselbe | p95 ≤ 10 ms |
| Allokation pro Abfrage | dasselbe | ≤ 250 KB |
| `Upsert` / `UpdateWeight` / `Remove` | einzeln, eigene Transaktion | p95 ≤ 1 ms |
| Aufbau | 3 Mio. Einträge, `Owned` | ≤ 3 min |
| Tippfehler-Wörterbuch | 700.000 verschiedene Wörter | ≤ 200 MB, Aufbau ≤ 10 s |
| Dateigröße | 1 Mio. Einträge, `MultiWord` | ≤ 300 MB inkl. Inhaltstabelle |

---

## 17. Pakete, Namespaces, Abhängigkeiten

### 17.1 Pakete

| Paket | Inhalt | Abhängigkeiten |
|---|---|---|
| `Nordmann.LUI.Abstractions` | Verträge: `ILuiIndex`, `ITextSimilarity`, Optionen, Ergebnisse | keine |
| `Nordmann.LUI` | die Umsetzung auf SQLite | Abstractions, `Microsoft.Data.Sqlite`, `symspell`, `F23.StringSimilarity` |
| `Nordmann.LUI.DependencyInjection` | `AddLui`, Keyed Services, `IHostedService` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions` |

### 17.2 Namespaces (Ordner = Namespace)

| Namespace | Inhalt |
|---|---|
| `Nordmann.LUI` | `LuiCatalog`, Builder |
| `Nordmann.LUI.Indexing` | Schema, Trigger, `Ensure`, `Sync`, `Check` |
| `Nordmann.LUI.Querying` | Abfrage-Ablauf, Kandidaten, Ranking |
| `Nordmann.LUI.Normalization` | `Normalizer`, SQL-Ausdrücke, Zuordnung für Highlight |
| `Nordmann.LUI.Typos` | Wörterbuch der Wortanfänge |
| `Nordmann.LUI.Similarity` | Ähnlichkeiten |
| `Nordmann.LUI.Payloads` | Serialisierung, Abbildung aus Spalten |

### 17.3 Abhängigkeiten

| Paket | Version | Lizenz |
|---|---|---|
| `Microsoft.Data.Sqlite` | 10.0.14 | MIT (natives SQLite über `SQLitePCLRaw.bundle_e_sqlite3`, Apache-2.0) |
| `symspell` | 6.7.3 | MIT |
| `F23.StringSimilarity` | 7.0.1 | MIT |

Mindestversion von SQLite: **3.45** (`remove_diacritics` für den
Trigram-Tokenizer); getestet mit 3.53.3. Die Bibliothek prüft die Version beim
Öffnen.

### 17.4 Übernahme aus dem bisherigen LUI

- die Arbeitsweise: ADRs, `AGENTS.md`, Messungen als Tests, Benchmark-Projekt,
  CI mit Paket-Smoke-Test, Paketierung (SourceLink, Symbole, Package Validation);
- die Distanzen als Vorlage (`IStringDistance` → `ITextSimilarity`);
- **nicht**: Roaring-Bitmaps, das Snapshot-Format, der Memory-Mapped-Reader,
  der Streaming-Build.

---

## 18. Grenzen und Nicht-Ziele

- Höchstens **4.294.967.295** Einträge pro Index (`doc_id` in 32 Bit der `rowid`).
- Gewicht **0 … 536.870.911**, Priorität **0 … 3**; eine Änderung von Gewicht
  oder Priorität ist intern ein Löschen und Einfügen im Suchindex (Trigger).
- Wer welche Priorität vergeben darf (z. B. „nur das System vergibt `System`"),
  entscheidet die Anwendung; LUI speichert die Priorität nur.
- Bei 1–2 Zeichen wird nur der **Anfang des Textes** gesucht (§ 21, O1).
- Kein Volltext-Ranking (BM25), kein Stemming, keine Synonyme, keine Facetten.
- Ein Schreiber pro Datenbankdatei (SQLite).
- Die Abfrage hat keine asynchrone Form (§ 13).

---

## 19. Teststrategie

| Stufe | Inhalt |
|---|---|
| **Unit** | `Normalizer` (Tabellen aus § 6), Wortanfang (Tabelle § 9.1), Ranking-Reihenfolgen, FTS5-Ausdruck (Escaping, Wörter < 3 Zeichen) |
| **Gleichheit SQL ↔ C#** | Für alle 685.829 Wörter der deutschen Wortliste und eine Liste von Sonderfällen liefern die SQL-Spalten und der C#-`Normalizer` dieselbe Suchform |
| **Integration** | `Ensure` (neu, idempotent, Abweichung), `Owned` und `Linked` (Insert, Update Text, Update Gewicht, Delete), `Sync`/`Check`/`Rebuild`, Payload nur für Treffer, Transaktion der Anwendung (Rollback lässt den Index unverändert) |
| **Goldene Abfragen** | die Beispiele aus Anhang B als feste Erwartungen (z. B. `apfelkuch` → Apfelkuchen auf Platz 1, Bratapfelkuchen hinter allen Wortanfängen; `gan` → Systemtag „vegan" auf Platz 1 auch bei 30.000+ Treffern) |
| **Qualität** | Hit@5 / Hit@20 auf dem Testkorpus (Anhang A), getrennt nach Länge, Tippfehler, Wortreihenfolge; Untergrenzen als Test |
| **Plan** | `EXPLAIN QUERY PLAN` der Kandidatenabfrage enthält `INDEX 64` und keinen `TEMP B-TREE` |
| **Nebenläufigkeit** | parallele Abfragen + gleichzeitiges `Upsert`; `Dispose` während laufender Abfragen wirft `ObjectDisposedException`, nie einen Absturz |
| **Leistung** | die Ziele aus § 16 als Budgets |

---

## 20. Umsetzungsplan

Jeder Schritt ist ein Pull Request mit ADR, Tests und Messung.

| # | Inhalt | fertig, wenn |
|---|---|---|
| 1 | Grundgerüst: Pakete, Namespaces, CI, `AGENTS.md`, `LuiCatalog.Open`, `lui_meta` | Build und Architekturtests grün |
| 2 | `Normalizer` in C# und SQL, Gleichheitstest | Gleichheit für die ganze Wortliste |
| 3 | `SingleWord` + `Owned`: Schema, Trigger, `Upsert`/`Remove`/`UpdateWeight`/`UpdatePriority`, `rowid` mit Priorität, Kandidaten, Präfix-Pfad, Ranking, Payload | Goldene Abfragen für `SingleWord` (inkl. Systemtag), Plan-Test, Leistungsziele |
| 4 | `MultiWord`: Zerlegung, `s_compact`, Wortanfang kompakt, N-gram Dice | Goldene Abfragen für `MultiWord` |
| 5 | Tippfehler-Toleranz | Qualitätsuntergrenzen für Tippfehler, Nebenläufigkeitstest |
| 6 | `Linked` | Integrationstests mit einer Tabelle der Anwendung, `allowSchemaChange` |
| 7 | `Sync`, `Check`, `Rebuild`, `Optimize` | Abgleich mit 1 Mio. Einträgen, Abbruch und Wiederaufsetzen |
| 8 | Fluent-Feinschliff, `Highlight`, DI-Paket, README, Beispiele | Paket-Smoke-Test mit README-Code |
| 9 | Leistungsbudgets und Benchmarks auf 3 Mio. Einträgen | alle Ziele aus § 16 als Tests grün |

---

## 21. Offene Fragen

| # | Frage | Vorschlag |
|---|---|---|
| O1 | Sollen 1–2 Zeichen bei `MultiWord` **jedes Wort** finden (`ap` → „Gedeckter **Ap**felkuchen")? | Später; braucht eine Tabelle der Wortanfänge pro Eintrag |
| O2 | `VIRTUAL` statt `STORED` für `s_norm`/`s_compact` — spart Platz, kostet bei `rebuild` | messen, dann entscheiden |
| O3 | Hashtags: zählen Großbuchstaben als Wortanfang (`cupfin` → `#WorldCupFinal`)? | als Option von `SingleWord` |
| O4 | Gewicht bei `Linked`: reicht eine Spalte, oder braucht es einen Ausdruck (z. B. `clicks + 10 * favorites`)? | Spalte; Ausdruck über eine berechnete Spalte der Anwendung |
| O5 | Mehrere Sprachen: reicht „deutsche Umschrift an/aus"? | ja für 1.0 |
| O6 | Filter bei `Linked` (nur `published = 1`)? | später, als `WHERE`-Bedingung in den Triggern |
| O7 | Gewichtung von Wortanfang und Gewicht weich mischen statt in Stufen? | erst mit echten Klickdaten entscheiden |
| O8 | Leere Eingabe: sollen die Einträge der Priorität `System` als Vorschlag kommen („Was schlägst du vor?")? | als Option `.SuggestWhenEmpty()` |
| O9 | Reichen 4 Prioritätsklassen? | ja; mehr kostet Bits des Gewichts |

---

## Anhang A: Messgrundlage

Alle Werte: 2026-11-01, Intel Xeon 2,1 GHz, 4 Kerne, 16 GB, Ubuntu 24.04,
.NET 10.0.14, SQLite 3.53.3, Datenbank im Page Cache. Synthetische Korpora;
Werte als Größenordnung, Vergleiche gelten innerhalb eines Laufs.

**A1 — Gewicht in der `rowid` (3 Mio. Tags, 50 % Gewicht 0, 300 Abfragen)**

| Treffer | `ORDER BY weight` (Spalte) | `rowid` DESC | **`rowid` ASC (E2)** | identisch |
|---|---:|---:|---:|:-:|
| 1–99 | 1,18 / 1,76 ms | 1,97 / 2,96 ms | 1,24 / 1,88 ms | 126/126 |
| 1.000–9.999 | 4,85 / 10,13 ms | 1,04 / 1,64 ms | 0,95 / 1,45 ms | 64/64 |
| 10.000–99.999 | 31,45 / 47,99 ms | 0,68 / 1,13 ms | 0,68 / 1,12 ms | 64/64 |
| 231.385 (`sch`) | 194 ms | 0,62 ms | — | ja |

**A2 — Ranking (3 Mio. Einträge aus der deutschen Wortliste, Ziel ∝ Gewicht)**

| Sortierung der 500 Kandidaten | Hit@5 | Hit@20 |
|---|---:|---:|
| nur Ähnlichkeit (Jaro-Winkler) | 13 % | 27 % |
| nur Gewicht | 40 % | 56 % |
| **Wortanfang → Gewicht** | **45 %** | **61 %** |

**A3 — Kandidatensuche bei mehreren Wörtern**

| Fall | als Ganzes | Wörter UND | + SymSpell |
|---|---:|---:|---:|
| zwei Wörter vertauscht (`kuch apfel`) | 0 % | 79 % (Dice 82 %) | — |
| Tippfehler in ganzen Wörtern | 0 % | 0 % | 76 % |

**A4 — Tippfehler in angefangenen Wörtern (1.200 Fälle, Hit@20)**

| | 4 Zeichen | 5 | 6 | 7 |
|---|---:|---:|---:|---:|
| ohne Tippfehler (Obergrenze) | 13 % | 42 % | 64 % | 75 % |
| SymSpell ganze Wörter | 1 % | 5 % | 14 % | 24 % |
| **SymSpell Wortanfänge, 3 Korrekturen (E6)** | 9 % | 29 % | 55 % | 69 % |

Wörterbuch der Wortanfänge (676.000 Wörter): 179 MB, Aufbau 5 s; Lookup 2–6 µs;
Abfrage mit 3 Korrekturen 4,4 / 8,4 ms (p50 / p95).

**A5 — Payload (3 Mio. Einträge, 182 Byte JSON)**

| | p50 | p95 | Allokation |
|---|---:|---:|---:|
| ohne Payload | 1,29 ms | 2,43 ms | 62 KB |
| **nur Treffer (E7)** | 1,62 ms | 3,14 ms | 108 KB |
| alle 500 Kandidaten | 1,62 ms | 2,88 ms | 184 KB |

**A6 — Inhaltsquelle (1 Mio. Einträge)**

| | Datei | Abfrage p50 / p95 | Aufbau |
|---|---:|---:|---:|
| Textkopie im Index | 241 MB | 1,53 / 2,95 ms | 46 s |
| contentless | 191 MB | 1,31 / 2,80 ms | 39 s |
| **external content + Trigger (E3)** | 253 MB | 1,98 / 3,54 ms | 60 s |

**A7 — Normalisierung durch SQLite allein (`remove_diacritics 1`)**

findet: `muller`→Müller, `munchen`→München, `cafe`→Café, `istanbul`→İstanbul ·
findet **nicht**: `mueller`→Müller, `strasse`→Straße, `lodz`→Łódź → daher E4.

**A8 — Priorität in der `rowid` (3 Mio. Einträge + Systemtag „vegan", Gewicht 5)**

| Eingabe | Treffer | ohne Priorität | mit Priorität `System` |
|---|---:|---|---|
| `gan` | 33.402 | nicht unter den 500 Kandidaten | Kandidat Nr. 1 |
| `veg` | 1.104 | nicht unter den 500 Kandidaten | Kandidat Nr. 1 |
| `vega` | 317 | Kandidat Nr. 164 | Kandidat Nr. 1 |

Plan in allen Fällen `SCAN … VIRTUAL TABLE INDEX 64` (ohne Sortierer).

---

## Anhang B: Erwartetes Verhalten aus Nutzersicht

Rezepttitel, `MultiWord`. Diese Fälle werden zu goldenen Tests (§ 19).

| Eingabe | erwartet vorn | erwartet weiter hinten |
|---|---|---|
| `a`, `ap` | die beliebtesten Rezepte, deren Titel so beginnt | — |
| `apfelk` | Apfelkuchen · Gedeckter Apfelkuchen · Apfel Kuchen | — |
| `apfelkuch` | Apfelkuchen · Gedeckter Apfelkuchen · Apfel Kuchen · Apfelkuchen mit Sahne | Bratapfelkuchen (auch wenn beliebter) |
| `apfel kuch` | dasselbe | — |
| `kuchen apfel` | Apfel Kuchen · Apfelkuchen | — |
| `apfelkuchen sahne` | Apfelkuchen mit Sahne | — |
| `äpfeln`, `aepfeln` | … mit Äpfeln | — |
| `strasse`, `Straße` | … Straße … | — |
| `apflekuchen` | Apfelkuchen (`CorrectedQuery = "apfelkuchen"`) | — |
| `gan`, `veg` (Tags) | **vegan** (Systemtag) ganz oben | alle Nutzer-Tags mit „gan" bzw. „veg" |

**Die Regeln, so wie der Nutzer sie spürt:** Bei 1–2 Buchstaben kommt das
Beliebteste. Was er tippt, ist der Anfang eines Wortes, und das kommt zuerst.
Unter diesen gewinnt, was am besten passt; bei Gleichstand das Beliebtere.
Leerzeichen, Wortreihenfolge und Umlaut-Schreibweise sind egal. Ein Tippfehler
führt nicht ins Leere. Jeder weitere Buchstabe macht die Liste genauer.
