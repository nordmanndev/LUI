# Performance-Audit LUI (`github.com/nordmanndev/LUI`)

**Stand:** `main` @ `320605e` (Merge PR #161, 2026-11-13) · **Referenzstand:** Tag `v0.1.0-alpha` @ `6578ebf` (2026-11-11)
**Geprüfte Quellen:** `README.md` (Zeilen 108–270), `docs/benchmarks.md` (590 Zeilen), `benchmarks/results/*.md` (5 Reports),
`tests/Nordmann.LUI.Testing/Budgets/budgets.json`, `tests/Nordmann.LUI.Performance.Tests/*`, `benchmarks/Nordmann.LUI.Benchmarks/*`,
`src/Nordmann.LUI/**` (QueryRunner, CandidateReader, Ranker, HitReader, TypoCorrection, EmbeddedSource, Database, EmbeddedIndex, WriteStatements),
`.agents/adr/**`, `docs/typo-dictionary.md`, `.github/workflows/performance.yml`.

**Eigene Messungen (Proben, kein Repository-Inhalt):** SQLite-Ebene mit dem Schema und dem Korpus des Repos
(`probes/probe_prefix_path.py`, `probes/probe_write_path.py`, `probes/probe_write_classes.py`, `probes/probe_checkpoint.py`),
SymSpell-Nachbau der Korrektur (`probes/symspell_probe/`), Läufe der Bibliothek selbst
(`probes/lui_probe/`, `probes/lui_probe3/`, `probes/lui_probe4/`) sowie **zwei eigene BenchmarkDotNet-Läufe** der
Klassen `CorrectionBenchmarks` und `QueryBenchmarks` (Kopie des Baums unter `/tmp`, .NET 10.0.404, SQLite über
`Microsoft.Data.Sqlite`), davon einer mit instrumentierter Report-Phase.
Alle Proben mit dem Schema, dem Korpus und den PRAGMAs des Repos, 200 000 Einträge, Datenbank auf lokalem
Datenträger (`/tmp`), sofern nicht anders genannt.

---

## 0. Zwei Korrekturen an der Aufgabenstellung (vorab)

**(a) Die Maschine der BenchmarkDotNet-Tabellen.** Die Aufgabenstellung nennt „Intel Xeon 6982P-C 3.80 GHz, 2 Kerne,
Debian 12". Das gilt für den Report vom **2026-11-06** (im Tag `v0.1.0-alpha`). Der Report auf `main` ist vom
**2026-11-13** und nennt **„Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores"**, Debian 12
(`benchmarks/results/Nordmann.LUI.Benchmarks.QueryBenchmarks-report-github.md`, Zeilen 2–3). Der 6982P-C-Stand ist
superseded (Commit `7026183`). Die Probe im Sandbox-Bestand liefert `Intel(R) Xeon(R) Platinum`, 2 CPUs, 3 GB RAM,
Debian 12 — dieselbe Maschinenklasse wie der aktuelle Report.

**(b) Die ±48 µs bei „apfelkuc".** Sie stammen aus **derselben** Runde: Im Report vom 2026-11-06 steht die Zeile
`Search | MultiWord | apfelkuc | Mean 238.05 μs | Error 48.295 μs | StdDev 140.112 μs | Median 152.11 μs`.
Die **±48,295 µs sind die Spalte „Error"** — die Konfidenzhalbbreite des **Mittels** (bei BDN die 99,9-%-CI), **keine
Perzentil-Streuung und kein Ausreißer**. Im aktuellen Report (2026-11-13) misst dieselbe Anfrage
`131.5 μs | Error 2.61 μs | StdDev 5.79 μs` — die Streuung ist **nicht reproduzierbar**, sie war eine Eigenschaft jenes Laufs.

---

## 1. Zusammenfassung (5 Sätze)

Die Budget-Tabelle der README erfüllt ihre Limits auf dem Papier — **alle zehn Zeilen**, eine davon exakt
(Tippfehler-Wörterbuch mit zwei Fehlern: 240 MB / ≤ 240 MB), die engsten beiden mit 91 % (Datei 457/500 MB)
und 86 % (Build 205,9/240 s) der Grenze. Die Belege dahinter sind jedoch
**aus vier verschiedenen Läufen von vier verschiedenen Code- und Korpusständen** (2026-11-05 bis 2026-11-13)
zusammengesetzt, und die Latenzzeilen der Budgets sind mit den eingecheckten Korpora **nicht mehr reproduzierbar**:
`words.txt` hat 1 908 798 Einträge, die Budgets-Szenarien verlangen 3 000 000. Die wöchentliche Mess-Lane
(`.github/workflows/performance.yml`) **prüft von 12 Budgets genau 3** (drei Größen) — alle Zeiten, alle Allokationen
und der 3-Millionen-Aufbau werden nur *berichtet*, und die Lane schreibt ihre Zahlen nirgends im Repository fest. Zwei
Artefakte widersprechen den Zahlen der Tabelle: der **Write-Report** (1,87–2,04 ms gegen ≤ 1 ms Budget) misst
nachweislich das Dateisystem statt der Bibliothek (gemessen: derselbe SQL-Plan 0,225 ms lokal gegen 1,846 ms auf
dem NAS-Mount), und der **Korrektur-Report** misst vier Anfragen, die **nichts finden** — die Fixture baut den Index
**ohne** `.TypoTolerance()`, das Wörterbuch wird nie gebaut, die Korrektur wird übersprungen (Hits = 0); eine
echte Korrektur kostet **3,6–9×** mehr (gemessen: 0,45 ms → 1,62 ms, 5,6 KB → 24,3 KB). Der größte vermeidbare
Hotspot ist der **Präfix-Pfad (1–2 Zeichen)**: er liest für bis zu 500 Kandidaten den **vollen Text** mit, obwohl
das Ranking (`WeightOnly`, ADR-0028) weder Text noch Wortanfang braucht — gemessen 0,975 ms → 0,267 ms und rund
20–25 KB weniger Allokation pro Anfrage, wenn nur die Rowid gelesen wird.

---

## 2. Budgets gegen Messwerte, Zeile für Zeile

Quellen: `README.md:119–130` (= `docs/benchmarks.md:21–32`), Grenzen aus `budgets.json`.

| Bereich | Messwert | Ziel / Limit | Abweichung | Bewertung |
|---|---|---|---|---|
| query p50 | 1,34–1,71 ms | ≤ 2 ms | 67–86 % des Limits | **erfüllt**, aber Szenario existiert nicht mehr (3 M), Werte vom 2026-11-05 mit dem alten, generierten Korpus → **nicht reproduzierbar** |
| query p95, 1–4 Zeichen | ≤ 3,1 ms | ≤ 5 ms | 62 % | **erfüllt**; die Schreibweise „≤" ist eine Obergrenze über Längen-Gruppen (1,21/1,45/1,97/3,11 ms), kein Messwert eines Laufs |
| query p95, 5–8 Zeichen | 13,4–17,5 ms | ≤ 20 ms | 67–88 % | **erfüllt** (Lauf 5/6, 2026-11-05); nicht reproduzierbar |
| query mit Tippfehler, p95 | 27,9–32,5 ms | ≤ 35 ms | 80–93 % | **erfüllt**; die 35-ms-Grenze ist eine **Lockerung** (ADR-0020/ADR-0010: vorher ≤ 10 ms) — dokumentiert |
| Allokation p50 / p95 | 46 / 51 KB | ≤ 60 / ≤ 100 KB | 77 % / 51 % | **erfüllt**; Zahl aus der Lane (2026-11-06/09) bei 3 M; ADR-0032 misst am selben Tag bei 200 k **18,09 / 38,80 KB** — die Tabelle nennt nur eine der beiden Messungen, ohne die Szene zu nennen |
| `Upsert`/`UpdateWeight`/`Remove`, p95 | 0,51–0,78 ms | ≤ 1 ms | 51–78 % | **erfüllt laut Budget-Test — widersprüchlich**: der eingecheckte Write-Report misst `UpdateWeight` 1 868 µs (2026-11-06) bzw. 2 037 µs (2026-11-13), also **2–4× über dem Limit**. Kein Lauf im Repo überbrückt die Differenz ⇒ **[Daten fehlen]** |
| erster Ladevorgang 3 M (`Sync`) | 172,5–205,9 s | ≤ 4 min (240 s) | 72–86 % | **erfüllt** (Batches 1 000 000); **nicht reproduzierbar**: das Korpus hat 1,9 M Einträge. Die Lane misst mit Batch **10 000** — eine andere Szene als die des Budgets |
| Tippfehler-Wörterbuch 685 739 Wörter | 138 MB, 4,1–4,8 s (4,3 s am 2026-11-12) | ≤ 200 MB, ≤ 10 s | 69 % / 41–48 % | **erfüllt**; Speicher ist maschinenunabhängig behauptet (Heap-Zuwachs), die Zeit gehört zur Maschine (`Xeon Platinum, 2 Kerne, 3,0 GB, Debian 12`) |
| Tippfehler-Wörterbuch, zwei Fehler | 240 MB | ≤ 240 MB | **100 %** | **exakt am Limit — kein Puffer**. Für die zwei Fehler gibt es ein **eigenes** Budget (≤ 240 MB), das auf den gemessenen Wert gesetzt wurde; ADR-0020s ≤ 200 MB galt dem **Ein-Fehler**-Wörterbuch (138 MB ⇒ 69 %). Das alte Versprechen ist ausdrücklich als verfehlt festgeschrieben (`docs/benchmarks.md:42–45`, `docs/typo-dictionary.md`) — nicht still gesenkt |
| Datei 1 M Titel | 457 MB | ≤ 500 MB | 91 % | **erfüllt**; die Grenze war ursprünglich ≤ 300 MB (verfehlt) und wurde angehoben — **der alte Wert steht bis heute in `docs/benchmarks.md:485`** (siehe Befund D1) |
| *implizit:* Budgets in `budgets.json` | 12 Budgets | — | **8 von 12 nennen 3 000 000 Einträge**; das Korpus hat 1 908 798 | **8 Budgets sind in ihrer Szene nicht messbar** (`docs/benchmarks.md:41–44` sagt das) — „reported, not asserted" |

**Was die Tabelle nicht sagt (Methodik, Frage 3):**

- **Datenstand:** Latenzen, Build und Dateigröße vom **2026-11-05** (CI-Maschine, Code vor der Allokations-Umstellung),
  Allokation vom **2026-11-06** (Lane), Tippfehler-Wörterbuch vom **2026-11-06** und **2026-11-12** (andere Maschine),
  BDN-Tabellen vom **2026-11-13** (anderer Korpus, anderes Harness, PR #161). Vier Läufe, vier Stände.
- **Anzahl der Läufe:** Der Budget-Test ist **ein einziger Durchgang** über 2 000 Eingaben je Fall
  (`BudgetPerformance.cs:146–159`); die eigene Regel 3 des Repos verlangt „old and new are measured alternately, and
  the **median** is taken" (`docs/benchmarks.md:86`) — für die Budgets wird sie nicht angewandt. BDN fährt 15 Iterationen
  mit 2–8 % CI; der Fehler der alten `ShortRun`-Tabelle betrug bis 260 % des Mittels (`docs/benchmarks.md:503`).
- **Warm-up:** Der Budget-Test wärmt mit 300 Anfragen (`BudgetPerformance.cs:141–144`), der BDN-Harness wärmt je Fall.
  Der Korrektur-Harness **ruft** ebenfalls das Warm-up des Wörterbuchs (`IndexFixture.WarmUp`) — es baut aber nichts,
  weil seine Fixture den Index ohne Tippfehlertoleranz anlegt (D7).
- **Korpusgröße:** Budgets 3 M / BDN 200 000 — Faktor 15. Die README sagt es selbst: die Tabellen sind „the code on the
  corpus of the measurement, **not the scenario of the budgets**" (`README.md:150`).
- **Perzentile:** Der Budget-Test rechnet ein **Nahestrang-Perzentil** über 2 000 Stichproben (p95 = der 1 900. Wert);
  die BDN-Tabellen kennen **überhaupt keine Perzentile**, nur Mean/Error/StdDev/Median. Ein „p95" aus der Tabelle der
  README lässt sich gegen die BDN-Tabellen **nicht** prüfen.

---

## 3. Vergleichbarkeit der Messungen (Frage 2)

| | Budget-Laufzeiten | BDN 2026-11-06 (im Tag) | BDN 2026-11-13 (auf `main`) | Tippfehler-Lauf 2026-11-12 |
|---|---|---|---|---|
| CPU | Intel Xeon 2,10 GHz | Xeon 6982P-C 3,80 GHz | Xeon Platinum 2,50 GHz | Xeon Platinum |
| Kerne | 4 | 2 | 2 | 2 |
| RAM | 15 GB | [Daten fehlen] | [Daten fehlen] | 3,0 GB |
| OS | Ubuntu 24.04 | Debian 12 | Debian 12 | Debian 12 |
| .NET | 10.0.14 | 10.0.14 | 10.0.14 | 10.0.403 |
| `journal_mode` | [Daten fehlen] | **nicht genannt** (der Report v. 2026-11-06 hat die Zeile nicht) | `wal` | [Daten fehlen] |
| **Datenträger der DB-Datei** | **[Daten fehlen]** | **[Daten fehlen]** | **[Daten fehlen]** | **[Daten fehlen]** |
| Korpus | generiert, 3 M (Buchstaben a–f bzw. 2,3 M Compounds) | generiert, 200 k | Dateien (Hash-Reihenfolge) | Wortliste 685 829 |

**Bewertung: in der Form vergleichbar, im Wert nicht.**

1. **CPU/Kerne:** Die Query-Benchmarks sind single-threaded (eine Anfrage je Iteration) ⇒ die Kernzahl wirkt auf die
   Latenz wenig; der Faktor zwischen 2,10 GHz/4 Kernen und 3,80 GHz/2 Kernen ist von der Größenordnung, die man
   erwarten würde — die BDN-Latenzen liegen denn auch **unter** den Budget-Latenzen (0,48 ms statt 1,34 ms für `b`),
   obwohl der Korpus 15× kleiner ist. Das ist plausibel, aber **kein Beweis**: dieselbe Anfrage auf einem 3-Millionen-
   Index ist nicht die Anfrage auf 200 000.
2. **RAM (15 GB vs. 3 GB) ist ein echter Unterschied, nicht ein Detail.** Beide Kataloge öffnen mit
   `CacheSizeMegabytes = 512` und `MemoryMapMegabytes = 2048` (`IndexFixture.cs:38`,
   `BudgetPerformance.cs:42`). Auf einem 3-GB-Runner kann ein 2-GB-mmap nicht vollständig resident werden; auf dem
   CI-Runner (15 GB) schon. Der Einfluss auf Latenz und Allokation ist **nicht gemessen** ⇒ **[Daten fehlen]**.
3. **Der Datenträger fehlt in jedem Report — und er ist der entscheidende Faktor für den Schreibpfad.** Probe
   (`probe_checkpoint.py`, `probe_write_classes.py`): derselbe UPDATE auf dem Schema des Repos,
   200 000 Einträge, WAL, `synchronous = NORMAL`, Autocommit:
   * lokaler Datenträger: p50 **0,092 ms**, p95 **0,225 ms**, p99 0,418 ms, Mittel 0,125 ms
   * NAS-Workspace-Mount: p50 0,093 ms, p95 **1,846 ms**, p99 **9,435 ms**, Max 97,9 ms, Mittel 0,478 ms
   Der **p95 ist 8×, der p99 22× schlechter** auf dem Netzwerkdateisystem. Die eingecheckten Write-Reports
   (1,87–2,04 ms) liegen genau in dieser Größenordnung. ⇒ **Die Write-Zeilen der BDN-Reports messen das Dateisystem,
   nicht die Bibliothek** (Hypothese H1, gestützt durch die Messung; der Report nennt den Datenträger nicht, also
   nicht beweisbar).
4. **Der Korpus ist zwischen den Budgets und den BDN-Tabellen nicht derselbe.** Die Budget-Latenzen (2026-11-05)
   entstanden mit `Corpus.Generate` — der Generator, dessen 200 000er-Präfix nur die Buchstaben a–f enthielt
   (Eigenbefund des Owners, `.agents/plan/2026-11-11-…:40`). Die BDN-Tabellen vom 2026-11-13 messen die **Hash-
   geordneten Korpusdateien** (ADR-0059). Ein Vergleich „Budget-p95 13,4 ms gegen BDN 0,8 ms" ist damit **kein
   Vergleich**, sondern ein Vergleich zweier Korpora.
5. **Die Zeit gehört zur Maschine — das Repo weiß es und handelt danach** (ADR-0041: `machine: "other"` ⇒ Zeiten
   werden berichtet, nicht verfehlt; Größen werden geprüft). Deshalb ist die Frage „sind die Werte vergleichbar?"
   in dieser Form nicht zu beantworten: **die Latenzen sind es nicht, die Größen (Bytes) sind es.**
6. **Zwei Läufe derselben Benchmark-Klasse, derselbe Code, derselbe Korpus, dieselbe Maschinenklasse: die
   Latenzen liegen 1,55–1,66× auseinander, die Allokationen auf dem Byte gleich** (4.4). Das ist der stärkste
   Einzelbefund zur Vergleichbarkeit: **eine Latenz ist ohne ihren Lauf keine Zahl.**

---

## 4. Hotspots (Frage 4)

### 4.1 Der Präfix-Pfad (1–2 Zeichen): ~0,5 ms, 40–43 KB — der teuerste vermeidbare Punkt

**Der Weg im Code**
1. `PreparedQuery.IsPrefixPath` — weniger als drei Codepoints ⇒ Präfix-Pfad (`PreparedQuery.cs:66`).
2. `CandidateReader.Command` wählt die Präfix-**Anweisung** statt FTS5 (`CandidateReader.cs:119–123`):
   `SELECT s_rowid, text FROM lui_<index> WHERE s_p1 = $q ORDER BY s_rowid LIMIT $n` mit `n = Candidates + 1 = 501`
   (`EmbeddedSource.cs:144–145`). `s_p1`/`s_p2` sind **STORED generierte Spalten**, indiziert über
   `(s_p1, s_rowid)` bzw. `(s_p2, s_rowid)` (`EmbeddedSource.cs:205–206`).
3. Das **Ranking ist für den Präfix-Pfad auf `WeightOnly` festgelegt** (ADR-0028, `QueryRunner.cs:85`) ⇒ **keine
   Ähnlichkeit** für die Kandidaten; mit `PriorityMode.First` (Default) liefert der Ranker die Kandidaten **sofort
   in der Rowid-Reihenfolge** zurück (`Ranker.cs:41–47`) ⇒ das Sortieren ist umsonst.
4. Die **Korrektur entfällt** im Präfix-Pfad (`QueryRunner.cs:155`).
5. **Trotzdem** berechnet der Kandidatenleser für **jeden** Kandidaten die Suchform und den Wortanfang
   (`CandidateReader.StartOf`, `CandidateReader.cs:137–172`) — 500 Normalisierungen. Das Ergebnis wird **nie benutzt**:
   `Ranker.GroupOf` wertet den Wortanfang nur aus, wenn `wordStartStage` wahr ist, und das ist für `WeightOnly`
   ausgeschlossen (`Ranker.cs:37`, `Ranker.cs:195–200`).

**Was das kostet — gemessen (SQLite-Ebene, Schema + Korpus des Repos, 200 000 Einträge, lokale Platte)**

| Anfrage | mit `text` (was LUI tut) | nur `s_rowid` | Anteil des Textes |
|---|---:|---:|---:|
| Präfix 1 Zeichen (`b`) | 0,492 ms (Median) | 0,416 ms | ~15 % |
| Präfix 2 Zeichen (`ka`) | 0,975 ms (Median), 0,523 ms (bester Lauf) | 0,267 ms (Median), 0,196 ms | **~72 %** |
| FTS5 4 Zeichen (`haus`) | 0,851 ms | — | — |
| FTS5 8 Zeichen (`apfelkuc`) | 0,324 ms (1 Treffer) | — | — |

Die BDN-Werte derselben Anfragen (476,4 µs für `b`, 517,5 µs für `ka` im Wort-Korpus) liegen auf den „mit Text"-Werten.
**Die Streuung derselben Anfrage (0,52–0,98 ms) ist der Page-Cache-Zustand**: 501 Zeilen-Lookups, über eine 44-MB-
Datei verteilt — jeder einzelne ist ein Sprung im B-Baum.

**Die Allokation** (40,02 KB Wörter / 43,62 KB Titel) erklärt sich aus 501 **Textstrings** aus SQLite (~30–60 B je
Kandidat), dem Kandidatenarray (500 × 24 B ≈ 12 KB) und dem Grundlast von ~7 KB (ADR-0032: „The ground load: about
7 KB per query"). Die Textstrings sind der größte einzelne Posten.

**Der Plan** (aus `EXPLAIN QUERY PLAN`, `probe_prefix_path.py`):
```
Präfix:  SEARCH lui_bench USING INDEX lui_bench_p1 (s_p1=?)      ← kein Sort, die Reihenfolge kommt vom Index
FTS5:    SCAN f VIRTUAL TABLE INDEX 64:M1
         SEARCH c USING INTEGER PRIMARY KEY (rowid=?)            ← ein PK-Sprung je Kandidat (ADR-0006)
```
Der Präfix-Pfad ist also **kein FTS5-Pfad**: ein Index-Scan auf eine generierte Spalte plus 501 Tabellen-Lookups.
Die Suche im FTS5-Pfad kostet primär das `MATCH` selbst (im Repo gemessen: „the price is in FTS5's MATCH"),
der Tokenizer ist ein Trigram-Tokenizer mit `remove_diacritics` (`EmbeddedSource.cs:163–166`); eine Trigram-Phrase
aus häufigen Trigrammen läuft die Posting-Listen bis zum Ende (ADR-0010; im Repo: „Rent Schwe" 54 ms für 38 Kandidaten).

### 4.2 Der Korrektur-Report misst vier Anfragen, die nichts finden — die Fixture lässt die Korrektur nicht zu

`benchmarks/results/…CorrectionBenchmarks-report-github.md` meldet für **alle vier** Eingaben **Hits = 0**
(bahmhof, fusbal, kartofel, strasee), bei 57,91–329,98 µs und 5,52–5,53 KB Allokation.

**Der Mechanismus (gemessen, nicht geraten).** `IndexFixture.CreateEmbedded` legt den Index **ohne**
`.TypoTolerance()` an:

```csharp
// benchmarks/Nordmann.LUI.Benchmarks/IndexFixture.cs:62–67
var index = corpus.Kind == CorpusKind.MultiWord
    ? _catalog.Create("bench", builder => builder.MultiWord())
    : _catalog.Create("bench", builder => builder.SingleWord());
```

Ein Index **ohne** Tippfehlertoleranz hat die Distanz `None` — und `None` **baut gar kein Wörterbuch** (ADR-0054,
Entscheidung D4 des Plans: „`None` builds no dictionary at all"). Das `WarmUp(true)` der Fixture baut deshalb
**nichts**, die Abfrage wird **ohne Korrektur** beantwortet, und die Kandidaten der Eingabe sind null, weil die
Eingaben absichtlich falsch geschrieben sind.

*Nachweis:* Ich habe den Report-Phase des Benchmarks in einer Kopie des Baums instrumentiert und die Klassen-Setup
nach dem Warm-up ausgegeben:

```
[probe] warm-up of the fixture: correction True, dictionary NotBuilt
```

*Zweite Probe* (`probes/lui_probe3/`, die Fixture der Benchmark-Zeile für Zeile nachgebaut, mit dem
`CorpusFile` des Repos): vor dem Warm-up `NotBuilt` → **0 Kandidaten, `CorrectionSkipped = True`**; nach dem
Warm-up `Current` → **72 / 99 / 118 / 402 Kandidaten**, korrigiert nach `kartoffel` / `bahnhof` / `ausbal` /
`strasse`. Der Korpus-Leser ist **nicht** die Ursache: derselbe Korpus, von Hand gelesen, ergibt dieselben Zahlen.

*Und die Korrektur selbst funktioniert* — der Wörterbuch-Nachbau mit den Parametern des Repos (symspell 6.7.3,
`maxDictionaryEditDistance = 1`, `prefixLength = 7`, `countThreshold = 1`, die 3–10-Codepoints-Anfänge):

| Eingabe | Vorschlag des Wörterbuchs | Kandidaten in der Bibliothek |
|---|---|---:|
| `kartofel` | `kartoffel` (Distanz 1) | 72 |
| `bahmhof` | `bahnhof` (Distanz 1) | 99 |
| `fusbal` | `fussbal`, `ausbal` (Distanz 1) | 118 |
| `strasee` | `strasse` (Distanz 1) | 402 |

**Was eine echte Korrektur kostet** (gemessen, `probes/lui_probe4/`: Index mit `.TypoTolerance()`, 200 000
Einträge, lokale Platte, Median von 101 Läufen):

| Eingabe | ohne Korrektur | mit Korrektur | Faktor | Allokation ohne → mit |
|---|---|---|---:|---|
| `kartofel` | 0,449 ms, 5,6 KB, 0 Kand. | **1,619 ms**, 24,3 KB, 72 Kand. | 3,6× | 4,3× |
| `bahmhof` | 0,111 ms, 5,6 KB, 0 Kand. | **1,005 ms**, 26,1 KB, 99 Kand. | 9,1× | 4,7× |
| `fusbal` | 0,204 ms, 5,6 KB, 0 Kand. | **1,751 ms**, 28,5 KB, 118 Kand. | 8,6× | 5,1× |
| `strasee` | 0,790 ms, 5,6 KB, 0 Kand. | **4,481 ms** (p95 15,3 ms), 43,9 KB, 402 Kand. | 5,7× | 7,8× |

⇒ **Der eingecheckte Korrektur-Report (57–330 µs, 5,52 KB) misst den unkorrigierten Pfad** — eine Anfrage, die
nichts findet. Der Preis der Korrektur, den das Repository für sich zu messen glaubt, ist damit **nirgends**
gemessen; die einzige Zahl, die etwas darüber sagt, ist das Budget `query-corrected-p95` (27,9–32,5 ms bei 3 M
Einträgen) — 6–30× höher als die 1,0–4,5 ms, die ich bei 200 000 Einträgen gemessen habe, also mit einer ganz
anderen Szene.

**Das ist ein Fehler im Harness, nicht in der Bibliothek** — eine Zeile (`IndexFixture.CreateEmbedded` /
`CreateLinked`: `.TypoTolerance()` ergänzen) macht die Benchmark-Klasse wieder das, was sie sein soll.


### 4.3 Was die Spalte „Hits" bedeutet (und wovon sie abhängt)

`Hits` = `CandidatesConsidered` — die **rangierten Kandidaten**, nicht die zurückgegebenen Treffer
(`ReportWriter.cs:118`). Sie ist vollständig durch die Korpuszusammensetzung erklärt (Probe über die echten
Korpusdateien):

| Anfrage | Wörter-Korpus (200 k) | Titel-Korpus (200 k) | im Report (Wörter/Titel) |
|---|---:|---:|---|
| `apfelkuc` | 1 | 0 | 1 / 0 ✔ |
| `haus` | 1 090 | 1 725 | 500 (Budget) / 500 ✔ |
| `b` / `ka` | ≥ 500 | ≥ 500 | 500 ✔ |

Die Korrektur-Report-Zeilen fallen aus dem Rahmen, weil die Korrektur dort nichts fand (4.2).

### 4.4 Wie stabil sind die Zahlen eines Reports überhaupt? (zwei Läufe, derselbe Code)

Ich habe die **QueryBenchmarks** auf derselben Maschinenklasse (Xeon Platinum, 2 Kerne, Debian 12, .NET 10)
noch einmal laufen lassen — derselbe Code, derselbe Korpus, dieselbe Fixture. Ergebnis:

| Zeile | eingecheckt (2026-11-13) | mein Lauf | Faktor | Allokation (beide) |
|---|---:|---:|---:|---:|
| titles/`apfelkuc` | 131,5 µs | 211,0 µs | 1,60× | 5,78 KB ✔ identisch |
| titles/`b` | 495,0 µs | 816,0 µs | 1,65× | 43,62 KB ✔ |
| titles/`haus` | 736,8 µs | 1 138,2 µs | 1,55× | 51,57 KB ✔ |
| titles/`ka` | 525,9 µs | 862,1 µs | 1,64× | 43,04 KB ✔ |
| words/`apfelkuc` | 191,2 µs | 297,7 µs | 1,56× | 7,07 KB ✔ |
| words/`b` | 476,4 µs | 791,4 µs | 1,66× | 40,02 KB ✔ |
| words/`haus` | 809,7 µs | 1 251,9 µs | 1,55× | 42,03 KB ✔ |
| words/`ka` | 517,5 µs | 847,2 µs | 1,64× | 40,09 KB ✔ |

**Die Allokationen stimmen auf das Byte, die Latenzen liegen um Faktor 1,55–1,66 daneben.** Das ist der
belastbarste Satz dieses Audits für die Frage der Vergleichbarkeit: **eine Latenz aus einem BenchmarkDotNet-Report
ist nur zusammen mit ihrem Lauf lesbar** — zwei Läufe derselben Benchmark-Klasse auf derselben Maschinenklasse
unterscheiden sich um 60 %, ohne dass sich der Code geändert hätte (bei mir .NET 10.0.15 statt 10.0.14 und ein
geteilter Runner; die Last des geteilten Runners ist die naheliegendste Ursache, sie ist nicht gemessen).
Die **Allokation** hingegen ist stabil — sie ist eine Größe, kein Zeitwert (ADR-0041).

**Die Verteilung ist nicht normal.** BenchmarkDotNet meldet für die Korrektur-Fälle selbst
„*It seems that the distribution is bimodal (mValue = 3.47)*" und entfernt in den Query-Fällen bis zu
**9 Ausreißer** je Fall (1,33–1,56 ms bei `haus`). Der Mittelwert einer bimodalen Verteilung ist keine Kennzahl,
die man mit einem Limit vergleichen kann — der Median (in der Spalte des Reports vorhanden) ist es. Das betrifft
auch die ±48,295 µs der Aufgabenstellung: sie sind die Konfidenzhalbbreite eines Mittelwerts aus einer
bimodalen Verteilung.

---

## 5. Skalierung und Speicher (Frage 5)

| Größe | Wert | Wachstum | Engpass |
|---|---|---|---|
| `Sync` 3 M Einträge | 172,5–205,9 s (Batches 1 000 000) | 1 M: 54,9 s (Batch 100 000) / 77,8 s (Batch 10 000) ⇒ **die Batchgröße wirkt stärker als die Reihenfolge** (ADR-0016) | Die **Anzahl der Commits**, nicht die Datenmenge; die Lane misst mit Batch 10 000, also in einer anderen Szene als das Budget |
| Tippfehler-Wörterbuch | 138 MB / 4,3 s (1 Fehler), 240 MB / 8,5 s (2 Fehler) | wächst **je Eintrag**, nicht je Datei: die Anfänge der Wörter (647 948) → 880 384 Wörterbucheinträge | Das **Arbeitsverzeichnis**: 1 483 MB belegt gegenüber 1 208 MB nach dem Umbau (ADR-0025); der gemeldete Wert ist der **Heap-Zuwachs** (`GC.GetTotalMemory`), nicht der Working Set |
| Datei 1 M Titel | 457 MB | linear: 91 MB (200 k) → 445/457 MB (1 M) ⇒ **~457 B je Titel** (36 Zeichen); Einzelwort: 925 MB je 3 M ⇒ ~308 B je Eintrag | Bei 5,17 M Wikipedia-Titeln wären es ~2,4 GB; der Grenzwert (500 MB) ist zu **91 %** ausgeschöpft und war ursprünglich 300 MB |
| Korpusdateien im Repo | 162 MB komprimiert / 432 MB roh | — | zwei Dateien über der GitHub-Empfehlung von 50 MB, ohne LFS (ADR-0059, Owner-Entscheidung) |

**Bewertung.** Das Wachstum ist **linear** und damit vorhersagbar; es gibt keinen superlinearen Punkt. Die beiden
engsten Stellen sind (a) das **Tippfehler-Wörterbuch** (138 MB Speicher für eine Funktion, die per Default aus ist —
korrekt so entschieden) und (b) die **Dateigröße** (91 % des Limits, Grenze nachträglich auf 300 → 500 MB angehoben).
Das 3-Millionen-Szenario ist mit den eingecheckten Korpora **nicht mehr herstellbar** — die eingecheckte
`performance.scenario.json` verlangt 200 000, die Lane 3 000 000, das Korpus hat 1 908 798 ⇒ **8 von 12 Budgets sind
„reported (not the scenario)"** und werden nicht geprüft.

---

## 6. Der Schreibpfad (Frage 6)

**Der Weg im Code.** Ein einzelnes `Upsert`/`UpdateWeight`/`Remove` **ohne** Anwendungs-Transaktion läuft im
**Autocommit** auf der langlebigen Writer-Verbindung des Katalogs (`Database.cs:78–91`); die Anweisungen sind
vorbereitet und gecacht (`WriteStatements.cs`), der `WriterGate` serialisiert die Schreibvorgänge (ein Writer pro
Datei). WAL ist Default (`LuiCatalogOptions.WriteAheadLog = true`), `synchronous = NORMAL`,
`busy_timeout = 5 s` (Default), der Provider wiederholt ein gesperrtes Kommando 1 s lang (ADR-0046).

**Der zentrale Befund: das Gewicht steckt in der FTS5-Rowid.**
`s_rowid = (priority << 61) | ((MaxWeight − weight) << 32) | doc_id` (`EmbeddedSource.cs:207`), und der Trigger
`lui_bench_au` feuert bei `old.s_rowid IS NOT new.s_rowid` ⇒ **jede Änderung eines Gewichts oder einer Priorität
schreibt den Eintrag im Suchindex neu** (ein `delete` + ein `insert` in FTS5, `EmbeddedSource.cs:238–245`).
Das ist der Preis des Designs, `ORDER BY f.rowid` die Gewichtsreihenfolge gratis zu liefern.

**Gemessen** (Schema + Korpus des Repos, 200 000 Einträge, Autocommit, WAL, `synchronous = NORMAL`, lokale Platte):

| Schreibvorgang | mit Trigger | ohne Trigger `lui_bench_au` | Faktor |
|---|---:|---:|---:|
| Upsert (gleicher Text, neues Gewicht) | p50 0,106 ms / p95 0,291 ms | p50 0,023 ms / p95 0,033 ms | **4,6×** |
| Gewichtsänderung | p50 0,101 ms / p95 0,209 ms | p50 0,016 ms / p95 0,024 ms | **6,3×** |
| Löschen + Zurück | p50 0,138 ms / p95 0,322 ms | p50 0,069 ms / p95 0,232 ms | 2,0× |
| alle drei Klassen | p50 0,114 ms / p95 0,299 ms | p50 0,023 ms / p95 0,159 ms | 5,0× |

Und zum Vergleich, **dieselbe Anfrage auf dem Netzwerkdateisystem** (dem Mount des Workspace):
p50 0,093 ms, **p95 1,846 ms**, p99 9,435 ms, Max 97,9 ms.

**Damit ist die Differenz zwischen Budget und Report erklärt** (Hypothese H1, gestützt, nicht bewiesen — der Report
nennt den Datenträger nicht):
* Der **Budget-Test** misst 600 gemischte Schreibvorgänge bei 3 M Einträgen auf der CI-Maschine
  (`BudgetPerformance.cs:187–215`) ⇒ p95 0,51–0,78 ms.
* Der **BDN-Report** misst dieselbe Art von Schreibvorgang über Tausende Iterationen ⇒ 1,87–2,04 ms; diese Größe
  liegt exakt in dem, was ein Netzwerkdateisystem aus derselben Anweisung macht (p95 1,846 ms).
⇒ **Die Write-Zeilen der Reports sind keine Aussage über die Bibliothek, solange der Datenträger nicht genannt wird.**
*Hypothese H2* (Checkpoint): 20 000 Schreibvorgänge ändern den Mittelwert **nicht** (0,122 ms) — der Checkpoint
(alle 1000 WAL-Seiten) taucht nur im Maximum auf (5,8 ms bei 600, 61 ms bei 20 000 Schreibvorgängen). H2 ist damit
**widerlegt**; der Unterschied steckt in der I/O-Absorption pro Commit, nicht in der Häufigkeit der Checkpoints.

**Was der Pfad sonst kostet.** `UpsertMany` ist 4,4× schneller als die Einzelschreibvorgänge (26 052/s gegen
5 913/s, `docs/benchmarks.md:339–344`); der Build-Pfad `Sync` mit Batches ist die Antwort auf den Aufbau. Ein
gesperrter Schreibvorgang wartet 5 s (`busy_timeout`) und wird dann mit `SQLITE_BUSY` abgewiesen — LUI serialisiert
die eigenen Schreibvorgänge, der Konflikt entsteht mit der Anwendung.

---

## 7. Befunde an den Artefakten (Fund, nicht Analyse)

| # | Befund | Nachweis |
|---|---|---|
| D1 | **`docs/benchmarks.md:485` enthält die alte Grenze ≤ 300 MB** für dieselbe Zahl 457 MB, die oben (Zeile 32) als „≤ 500 MB erfüllt" steht — **dieselbe Zahl, zwei Verdikte** im selben Dokument. Der Wächter `ReadmeBudgetsTests` sieht diese Tabelle **nicht**: er prüft die Tabellen mit den Köpfen „Target \| Measured \| Limit" (README), „Measurement \| Scenario \| Target" und „Target \| Measured at the scenario \| Limit" (Dokument) — die Tabelle in Zeile 482 trägt den Kopf „Target \| Measured \| Limit" und rutscht damit durch. | `ReadmeBudgetsTests.cs:27–67`, `docs/benchmarks.md:482–486` |
| D2 | **Die README wiederholt denselben Absatz zweimal** (Zeilen 131–136 und 138–148) — die zweite Fassung ergänzt den Tippfehler-Lauf vom 2026-11-12, die erste bleibt stehen. Am Tag `v0.1.0-alpha` war der Absatz noch einfach (Merge-Artefakt aus PR #161). | `README.md:129–148`, `git show v0.1.0-alpha:README.md` |
| D3 | **Die Mess-Lane schreibt ihre Zahlen nirgends im Repository fest.** Der Workflow-Kommentar sagt „The record of the numbers stays docs/benchmarks.md" — der Lauf schreibt aber nur in die Step-Summary der GitHub-Action (`grep -F '[perf]' … | tee -a "$GITHUB_STEP_SUMMARY"`), die nach dem Lauf verschwindet. Der letzte dauerhafte Stand im Repository ist der 2026-11-12. | `.github/workflows/performance.yml:100–112` |
| D4 | **Die Lane prüft 3 von 12 Budgets.** `machine: "other"` ⇒ Zeiten werden nur berichtet; `entries >= ScenarioEntries` ⇒ mit 1 908 798 Korpus-Einträgen sind alle Budgets mit Szenario 3 000 000 „reported (not the scenario)". Geprüft werden nur `typo-dictionary-size`, `typo-dictionary-two-errors-size` und `file-size-1m` — drei Größen. **Keine Latenz, keine Allokation und kein Aufbau werden von irgendetwas geprüft.** | `PerformanceBudget.cs:80–95`, `budgets.json`, `performance.scenario.json` |
| D5 | **Die Grenze „≤ 240 MB" für das Zwei-Fehler-Wörterbuch ist mit 240 MB exakt erreicht** — 100 % des Limits, ohne Reserve. (Das eigene Budget für zwei Fehler wurde auf den gemessenen Wert gesetzt; ADR-0020s ≤ 200 MB galt dem Ein-Fehler-Wörterbuch.) | `README.md:129`, `docs/typo-dictionary.md`, `docs/benchmarks.md:42–45` |
| D6 | Die Spalte „Error" der BDN-Tabellen ist die **Konfidenzhalbbreite des Mittels** (BDN-Legende: „Half of 99.9% confidence interval"), keine Streuung und kein Perzentil — die Bezeichnung lädt zu Fehldeutungen ein (die ±48 µs der Aufgabenstellung). | `benchmarks/results/…QueryBenchmarks-report-github.md`, BDN-Legende im Log |
| **D7** | **Die `CorrectionBenchmarks` messen eine Funktion, die die Fixture abgeschaltet hat** (4.2): `IndexFixture.CreateEmbedded` erzeugt den Index ohne `.TypoTolerance()` ⇒ Distanz `None` ⇒ **kein Wörterbuch** ⇒ die Korrektur wird übersprungen, die Abfrage findet nichts (Hits = 0). Die eingecheckten Zeilen messen den unkorrigierten Pfad (57–330 µs, 5,52 KB); eine echte Korrektur kostet 3,6–9× und 4–8× Allokation. **Fix: eine Zeile.** | `benchmarks/Nordmann.LUI.Benchmarks/IndexFixture.cs:62–67` und `:73–78`, `probes/lui_probe3/`, `probes/lui_probe4/`, instrumentierter Benchmark-Lauf („dictionary NotBuilt" nach dem Warm-up) |
| D8 | **Zwei Läufe derselben Benchmark-Klasse auf derselben Maschinenklasse unterscheiden sich um 1,55–1,66× in der Latenz** — bei identischen Allokationen auf dem Byte (4.4). Die Latenzzeilen eines Reports sind damit ohne ihren Lauf nicht zitierfähig. | eigener Lauf der QueryBenchmarks gegen `benchmarks/results/…QueryBenchmarks-report-github.md` |

---

## 8. Empfehlungen (priorisiert)

| # | Empfehlung | Wirkung | Aufwand |
|---|---|---|---|
| **R1** | **Den Write-Benchmark auf einem lokalen Datenträger laufen lassen und den Datenträger in jeden Report schreiben** (eine Zeile in `ReportWriter`, ein `TMPDIR`/`Data Source` in `tools/benchmarks.sh`; der Baum muss vollständig kopiert werden). | **Hoch.** Die Write-Zeilen werden überhaupt erst interpretierbar; die 2,04 ms gegen das ≤ 1 ms des Budgets lösen sich als Artefakt des Dateisystems auf (gemessen: p95 0,225 ms lokal gegen 1,846 ms auf dem NAS). Ohne das bleibt der wichtigste Schreibpfad unbelegt. | **Klein** (2 Zeilen Code + 1 Zeile Doku), Messung ~10 min |
| **R2** | **Die `CorrectionBenchmarks` wieder das messen lassen, was sie messen sollen:** in `IndexFixture.CreateEmbedded`/`CreateLinked` `.TypoTolerance()` ergänzen (eine Zeile) und im Harness **prüfen, dass `CandidatesConsidered > 0`** (eine Zeile mit 0 muss ein Fehler sein, wie das Repo es bei den Trefferzahlen selbst verlangt). | **Hoch.** Heute misst der Report vier Anfragen, die nichts finden und deren Korrektur abgeschaltet ist — der Preis der Tippfehler-Toleranz, das einzige unterscheidende Merkmal der Bibliothek, ist **nirgends** gemessen. Nach der Reparatur stehen 1,0–4,5 ms (p95 bis 15,3 ms) und 24–44 KB statt 57–330 µs und 5,52 KB in der Tabelle. | **Klein** (zwei Zeilen Code), Messung ~5 min |
| **R3** | **Im Präfix-Pfad nur die Rowid lesen.** Die Kandidatenanweisung holt `text` für bis zu 500 Kandidaten, obwohl das Ranking `WeightOnly` ist (ADR-0028) und der Text erst für die ≤ 20 Treffer gebraucht wird — der Treffer holt ihn sich aus der **ohnehin vorhandenen** Hit-Anweisung (`HitReader`, `SELECT doc_id, key, payload …`). | **Hoch (Latenz + Allokation).** Gemessen: 2 Zeichen 0,975 → 0,267 ms (**−73 %**), 1 Zeichen 0,492 → 0,416 ms; ~500 Strings weniger pro Anfrage (**−20…−25 KB** von 40–43 KB). Betroffen ist die häufigste Interaktion (die ersten Buchstaben einer Eingabe). | **Mittel** (Kandidatenanweisung, Hit-Anweisung, `Scored.Text` je Treffer) |
| **R4** | **Die Wörterbuch-Korrektur im Präfix-Pfad abschaffen:** den Wortanfang nur berechnen, wenn das Ranking ihn braucht (`wordStartStage`). Heute werden für jeden Präfix-Kandidaten 500 Suchformen erzeugt und verworfen. | **Mittel.** Entfernt 500 Normalisierungen je Präfix-Anfrage (die TextBenchmarks messen 50–114 ns je Suchform) und die `ArrayPool`-Vermietung pro Kandidat. Zusammen mit R3 wird der 1–2-Zeichen-Pfad zum reinen Index-Scan. | **Klein–Mittel** (ein Flag vom QueryRunner in den Kandidatenleser) |
| **R5** | **Die Mess-Szene wiederherstellbar machen und die Lane schreibfähig machen:** entweder ein Korpus mit ≥ 3 000 000 Einträgen (die Wörterliste hat 1,9 M) oder die Szenarien der Budgets auf die vorhandenen Korpora neu entscheiden (Owner-Entscheidung, ADR-0056); die Lane schreibt ihre `[perf]`-Zeilen in eine Datei im Repository (oder in ein veröffentlichtes Artefakt mit Datum und Maschine). | **Hoch (Nachhaltigkeit).** Heute sind 8 von 12 Budgets in ihrer Szene nicht messbar, die Lane prüft 3 Größen, und der letzte dauerhafte Messstand ist vom 2026-11-12 — die Schlagzeilenzahlen der README sind 8 Tage älter als die Artefakte daneben und niemals automatisch erneuert. | **Mittel** (Korpusfrage ist eine Owner-Entscheidung; die Lane braucht einen Schreibpfad) |

---

## 9. Offene Datenlücken (vor einer belastbaren Bewertung zu schließen)

1. **[Daten fehlen] Der Datenträger der Benchmark-Läufe.** In keinem Report genannt. Er ist nachweislich der
   Faktor, der die Write-Zeilen um Faktor 8–22 verschiebt (Probe). Ohne ihn ist der Write-Report nicht bewertbar.
2. **[Daten fehlen] Das Budget `write-p95` gegen den Write-Report.** 0,51–0,78 ms (600 gemischte Schreibvorgänge bei
   3 M) gegen 1,87–2,04 ms (einzelne Schreibvorgänge bei 200 k). Kein Lauf im Repository überbrückt die Differenz;
   die eine plausible Erklärung (Netzwerkdateisystem) ist eine Hypothese, weil der Datenträger nicht genannt ist.
3. **[Daten fehlen] Der Preis einer Korrektur auf der Maschine des Budgets.** Der Mechanismus ist jetzt bekannt
   (D7) und der Preis bei 200 000 Einträgen gemessen (1,0–4,5 ms, p95 bis 15,3 ms, 24–44 KB) — **auf der CI-Maschine
   bei 3 M Einträgen** ist er nicht gemessen; das Budget `query-corrected-p95` (27,9–32,5 ms) stammt aus dem Lauf vom
   2026-11-05 mit dem alten, generierten Korpus.
4. **[Daten fehlen] Das Budget `write-p95` gegen den Write-Report.** 0,51–0,78 ms (600 gemischte Schreibvorgänge bei
   3 M) gegen 1,87–2,04 ms (einzelne Schreibvorgänge bei 200 k). Kein Lauf im Repository überbrückt die Differenz;
   die eine plausible Erklärung (Netzwerkdateisystem) ist eine Hypothese, weil der Datenträger nicht genannt ist.
5. **[Daten fehlen] Das 3-Millionen-Szenario.** `words.txt` hat 1 908 798 Einträge ⇒ `build-3m`, `query-p50`,
   `query-p95-short`, `query-p95-long`, `query-corrected-p95`, `allocation-p50/p95` und `write-p95` sind in ihrer
   Szene nicht messbar. Die Kopfzahlen der README (172,5–205,9 s; 1,34–1,71 ms; 46/51 KB) stammen aus Läufen mit
   dem **alten, generierten** Korpus und sind mit den eingecheckten Dateien nicht reproduzierbar.
6. **[Daten fehlen] Der Einfluss von RAM und `mmap_size`.** Beide Kataloge öffnen mit 512 MB Cache und 2048 MB
   mmap; der aktuelle BDN-Lauf fand auf einer Maschine mit 3 GB statt. Wie viel davon auf Latenz und Allokation
   durchschlägt, ist nicht gemessen.
7. **[Daten fehlen] Die Persistenz der Messreihe.** Die Lane schreibt nur in die Step-Summary; im Repository steht
   kein Lauf nach dem 2026-11-12. Eine Trendreihe (wöchentlich, ADR-0041) existiert faktisch nicht.
8. **[Daten fehlen] Die Statistikbasis der Budget-Latenzen.** Der Budget-Test ist ein einziger Durchgang über 2 000
   Eingaben; die eigene Regel des Dokuments verlangt „alternierend messen und den Median nehmen". Wie stabil die
   1,34–1,71 ms sind, ist daraus nicht ableitbar — der Vergleich zweier Benchmark-Läufe (4.4) zeigt, wie weit
   gleiche Anfragen auf gleicher Hardware auseinanderliegen können.
9. **Bereinigt werden sollte (kein Datenmangel, sondern Widerspruch):** D1 (≤ 300 MB in `docs/benchmarks.md:485`),
   D2 (doppelter README-Absatz), D5 (Grenze ohne Reserve), D7 (Fixture ohne Tippfehlertoleranz).

---

## 10. Was ich verifiziert habe — und wie

| Nachweis | Weg |
|---|---|
| Die Budget-Tabelle stimmt mit `budgets.json` überein (Grenzen) | gelesen + mit dem Wächter `ReadmeBudgetsTests` verglichen (der Wächter prüft drei Tabellen — die vierte in `docs/benchmarks.md` sieht er nicht) |
| Die Grenze des Zwei-Fehler-Wörterbuchs wurde nach der Messung angehoben | `docs/typo-dictionary.md`, ADR-0054, `budgets.json` — dokumentiert, nicht still |
| Der Präfix-Pfad liest `text` je Kandidat und sortiert nicht | `EmbeddedSource.cs:144–145`, `EmbeddedSource.cs:205–206`, `EXPLAIN QUERY PLAN` (Probe) |
| Das Ranking braucht im Präfix-Pfad weder Text noch Wortanfang | `QueryRunner.cs:85`, `Ranker.cs:37–47`, `Ranker.cs:195–200` |
| Eine Gewichtsänderung schreibt den FTS5-Eintrag neu | `EmbeddedSource.cs:207` + `:238–245` — und gemessen (Faktor 4,6–6,3×) |
| Die BDN-Latenzen des Präfix-Pfades | SQLite-Probe auf Schema + Korpus des Repos: 0,492 ms gegen 476,4 µs im Report |
| Die Korrektur funktioniert auf genau diesem Korpus | SymSpell-Nachbau (6.7.3, Parameter des Repos) **und** Lauf der Bibliothek selbst: 72/99/118/402 Kandidaten |
| **Der Korrektur-Report misst den unkorrigierten Pfad** | Fixture nachgebaut (`probes/lui_probe3/`): nach dem Warm-up `NotBuilt` → 0 Kandidaten; mit `.TypoTolerance()` → `Current` → 72/99/118/402. Der instrumentierte Benchmark-Lauf bestätigt: „warm-up of the fixture: correction True, dictionary **NotBuilt**" |
| Der Preis einer echten Korrektur | `probes/lui_probe4/`: 1,005–4,481 ms (p95 bis 15,3 ms), 24,3–43,9 KB gegen 0,111–0,790 ms und 5,6 KB ohne Korrektur |
| Wie stabil ein Report ist | eigener Lauf der QueryBenchmarks: Allokationen auf dem Byte identisch, Latenzen 1,55–1,66× höher (4.4) |
| Die „Hits"-Spalte | über die echten Korpusdateien nachgerechnet: 1/0/1090/1725/500 ✔ |
| Der Dateisystem-Faktor im Schreibpfad | dieselbe Anweisung, lokale Platte gegen NAS-Mount: p95 0,225 ms gegen 1,846 ms |
| Die Report-Provenienz | `git show v0.1.0-alpha:benchmarks/results/…` gegen `git show cf686c0:…` gegen `main` |

---

## 11. Nachtrag (2026-11-14): die drei Lesepfad-Vorschläge des Owners, gemessen

Der Owner schlug drei Änderungen am Lesepfad vor: `mmap_size` je Verbindungsaufbau,
wiederverwendete Anweisungen (prepared statements) und eine read-only-Verbindung für die
Abfragen. Alle drei sind hier gemessen, bevor eine Zeile im Repository geändert wurde
(`git status` in `/workspace/LUI`: 0 Änderungen).

### 11.1 Die Messanlage

1 M Einträge (227 MB Datei), FTS5-Pfad (`haus`, 501 Kandidaten), 200 Wiederholungen,
p50 aus 200 Läufen. Quellen: `probes/lui_probe6/Program.cs` (prepared statements),
`probes/probe_journal_mode.py` und `probes/probe_journal_mode_realistic.py` (Journal-Modus),
`/tmp/LUI-run/audit-probe5` (gehaltene Verbindung, mmap, read-only).

### 11.2 Ergebnisse

| Frage | Messung | Quelle |
|---|---|---|
| **Gehaltene read-only-Verbindung statt einer je Anfrage** | Kandidatenanweisung 1,093 ms → **0,652 ms** (−40 %); zweiter Lauf auf demselben Sandbox: −7 % ⇒ die Latenzmessung ist auf 2 geteilten Kernen verrauscht | `audit-probe5`, Teil 1 |
| **Allokation derselben Änderung** | **3 560 B → 576 B je Anfrage** (−2 984 B, stabil) | `audit-probe5`, Teil 1 |
| **`PRAGMA mmap_size` je Verbindungsaufbau** | 0,022 ms → **0,018 ms** ohne mmap (also +4 µs *Kosten*) — und es bringt **nichts**: lokal 0,490 → 0,474 ms, auf dem NAS-Mount 3,440 → 3,305 ms | `audit-probe5`, Teil 2; `probe_mmap.py` |
| **read-only-Verbindung auf einer WAL-Datei** | liest ✓; SQLite legt die **`-shm` neu an** (32 KB, gemessen) ⇒ **das Verzeichnis muss schreibbar sein**; ein wirklich schreibgeschütztes Verzeichnis braucht `immutable=1` | `audit-probe5`, Teil 3 |
| **read-only auf einem heißen WAL** (Absturz des Schreibers) | öffnet ohne Fehler; die nicht committete Zeile bleibt unsichtbar ✓ | `probe_hotwal` (inline) |
| **`SqliteCommand.Prepare()`** | funktioniert mit **benannten** Parametern (`$q`, `$expr`, `$n`), in beiden Reihenfolgen (Transaktion zuerst / Prepare zuerst). Anonyme `?`-Platzhalter scheitern: Microsoft.Data.Sqlite bindet **nach Namen** | `probes/lui_probe6/Program.cs` |
| **Journal-Modus `DELETE` statt `WAL`**, realistisch (der Leser hält seine Momentaufnahme 2 ms wie eine echte Abfrage) | der Schreiber wartet **5,65 ms** statt **1,81 ms** und kommt durch; läuft eine Abfrage länger als der busy_timeout (5 s), scheitert der Schreibvorgang mit `SQLITE_BUSY` | `probes/probe_journal_mode_realistic.py` |
| **Journal-Modus `DELETE`**, der Leser hält die Momentaufnahme 1 s | der Schreiber wartet 1 001,7 ms und scheitert mit `database is locked` | `probes/probe_journal_mode.py` |
| **Verbindungsparameter von Microsoft.Data.Sqlite 10.0.14** | `Cache, Command Timeout, Data Source, Default Timeout, Foreign Keys, Mode, Password, Pooling, Recursive Triggers, Vfs` — **kein Journal-Modus** ⇒ der Journal-Modus kann nur per `PRAGMA journal_mode` gesetzt werden, nicht per Verbindungsstring | `probe7` (Reflection) |

### 11.3 Was daraus für den Entwurf folgt

- **Die gehaltene Verbindung ist der Hebel, nicht das Vorbereiten.** Microsoft.Data.Sqlite hat
  selbst einen Statement-Cache je Verbindung ⇒ mit gehaltener Verbindung ist die SQL-Ebene
  ohnehin einmalig vorbereitet. Das Vorbereiten macht es explizit, der Nutzen ist der
  gleiche.
- **Der Präzedenzfall liegt im Repository:** ADR-0009 („Writes use prepared statements on one
  writer connection") — der Schreibpfad macht seit 2026-11-02 genau das (184 µs → 59 µs je
  Eintrag). Der Lesepfad ist der einzige Pfad, der es noch nicht tut.
- **`Open` schreibt heute zweimal** (`PRAGMA journal_mode = WAL` und
  `CREATE TABLE IF NOT EXISTS lui_meta`, `IndexStore.Initialize`) — das widerspricht der
  Notiz in ADR-0033/ADR-0062 („`Open` writes nothing") und verschmutzt eine fremde Datei
  mit einer `lui_meta`-Tabelle.
- **Der Versionsstand verbietet das Wegstreichen:** `v0.1.1` ist getaggt ⇒ der Freeze aus
  ADR-0026 gilt ⇒ `MemoryMapMegabytes`, `WriteAheadLog` und `LuiReadOnlyException` bleiben
  im öffentlichen API.
