# Der große PR für 0.3.0 — Plan

Ein Pull Request, 0.3.0, die vier Aufzeichnungen 0064–0067 und die Befunde des Audits
(`/workspace/output/lui-performance-audit.md`, §7 D1–D8 und §8 R1–R5).

**20 Commits, jede Regel rot zuerst.** Jede Zeile nennt ihr Issue — die Nummer der Zeile ist
das Issue, die Umsetzung beginnt dort.

**Die Reihenfolge der Commits ist Teil des Vertrags:** erst der Beweis (ein Test, der auf dem alten Code rot ist),
dann der Fix. Der rote Zwischenstand bleibt im Verlauf des Branches stehen — er ist der Beweis, dass es den roten
Zustand gab. Weil die CI einen roten Lauf wegkassieren kann (`cancel-in-progress: true`), wird der rote Commit
**zuerst** gepusht, sein Lauf gelesen, und **dann** der Fix gepusht.

## Teil 1 — Die Aufzeichnungen (der Kern)

| # | Issue | Commit | Der rote Beweis | Der Fix | Die Dateien |
|---|---|---|---|---|---|
| 1 | #169 | **Die Pragmas sind ein Typ** | ein Test, der `options.Pragmas.BusyTimeout` liest (kompiliert nicht, bis der Typ da ist ⇒ der erste Commit ist der Typ **ohne** Verhalten) + ein Verhaltenstest: `JournalMode = Delete` ⇒ die Datei bekommt kein WAL | `LuiPragmas` + `LuiSynchronous` — **keine** `LuiJournalMode`: der Journal-Modus ist kein Parameter (ADR-0064 §3); die vier Mitglieder wandern aus `LuiCatalogOptions` | `LuiPragmas.cs`, `LuiCatalogOptions.cs`, `Database.cs` |
| 2 | #169 | **Der Modus einer Datei, die LUI nicht anlegt, bleibt** | der Test: eine Datei mit einer Tabelle ⇒ `PRAGMA journal_mode` bleibt `delete`, auch wenn die Optionen etwas anderes fordern | die Regel aus 0064 §3 (eine Datei ohne Tabelle wird eingerichtet, eine mit bleibt) | `Database.cs`, `IndexStore.cs` |
| 3 | #171 | **`Open` schreibt nichts** | der Test: `LuiCatalog.Open` auf einer frischen Datei ⇒ der Hash der Datei **vorher == nachher** und keine `lui_meta`-Tabelle (heute: rot) | `IndexStore.Initialize` fällt weg; das Öffnen prüft die Datei, der Schreiber legt `lui_meta` an | `IndexStore.cs`, `LuiCatalog.cs` |
| 4 | #171 | **Die Datei, die kein Katalog ist, wird abgelehnt** | der Test: `Open` auf einer fremden Datei ⇒ ein Fehler, der die fehlende Tabelle nennt (heute: rot, heute wird die Datei verschmutzt) | die Prüfung beim Öffnen | `LuiCatalog.cs`, `IndexStore.cs` |
| 5 | #171 | **Kein Katalog-Read-only-Schalter** | der Test: ein Katalog, dessen Verbindungsstring `Mode=ReadOnly` sagt ⇒ ein Schreibvorgang ⇒ `LuiReadOnlyException` (heute: rot über `ThrowIfReadOnly`, heute über SQLite) | `Database.ReadOnly` und `ThrowIfReadOnly` fallen weg; SQLite entscheidet, der Fehler wird übersetzt | `Database.cs`, `EmbeddedIndex.cs`, `LinkedIndex.cs`, `IndexStore.cs` |
| 6 | #170 | **Der Lesepfad öffnet read-only** | der Test: eine Datei, die **nicht** geschrieben werden darf, beantwortet eine Abfrage (heute: rot — der Lesepfad öffnet eine schreibbare Verbindung) | der Lesepfad öffnet `Mode=ReadOnly`; die Pragma-Anweisung einer Verbindung **ohne** `journal_mode` (read-only kann ihn nicht setzen — SQLite-Fehler 8) | `Database.cs`, `QueryRunner.cs`, `IndexStore.cs` |
| 7 | #170 | **Die Anweisungen sind vorbereitet** | der Test: die Anweisung des Lesepfads ist vorbereitet (`SqliteCommand.Prepare()`), nicht nur ausgeführt | `Prepare()` auf den drei Anweisungen des Lesepfads | `CandidateReader.cs`, `HitReader.cs`, `TypoCorrection.cs` |
| 8 | #170 | **Die Transaktion endet, bevor die Verbindung zurück in den Pool geht** | der Test: nach einer Abfrage ist die Verbindung des Pools wieder im Autocommit (`sqlite3_get_autocommit` ist wahr) — keine offene Transaktion im Pool | der Lesepfad hält **eine** Transaktion für die Kandidaten und die Treffer (ADR-0065 §4) und beendet sie **vor** dem Zurückgeben in den Pool | `QueryRunner.cs`, `Database.cs` |
| 9 | #173 | **Der Präfix-Pfad liest nur die Rowid** | der Test: die Kandidatenanweisung des Präfix-Pfads holt **keine** Spalte `text` (der Test liest den SQL-Text der Anweisung) — und der Präfix-Pfad liefert dieselben Treffer | die Anweisung des Präfix-Pfades und die Hit-Anweisung | `Schema/EmbeddedSource.cs`, `CandidateReader.cs`, `HitReader.cs` |
| 10 | #174 | **Kein Wortanfang auf dem Präfix-Pfad** | der Test: der Präfix-Pfad berechnet **keine** Suchform (ein Zähler am Normalisierer) | das Ranken entscheidet, ob der Wortanfang gebraucht wird | `CandidateReader.cs`, `QueryRunner.cs`, `Ranker.cs` |
| 11 | #172 | **Die Migration** | — | die CHANGELOG-Notiz mit dem Vorher und Nachher, `docs/api.md`, der ADR-Index, `docs/benchmarks.md` | `CHANGELOG.md`, `docs/api.md`, `.agents/adr/README.md` |

## Teil 2 — Die Befunde des Audits

| # | Issue | Commit | Der rote Beweis | Der Fix |
|---|---|---|---|---|
| 12 | #181 | **D1 — die alte Grenze in `docs/benchmarks.md`** | der Wächter `ReadmeBudgetsTests` prüft **alle** Tabellen mit dem Kopf „Target \| Measured \| Limit" ⇒ der Fehler wird rot | die Grenze auf ≤ 500 MB korrigieren, der Wächter prüft die vierte Tabelle mit |
| 13 | #182 | **D2 — der doppelte README-Absatz** | ein Wächter: kein Absatz steht zweimal in der README | den alten Absatz streichen |
| 14 | #179 | **D7/R2 — die Korrektur-Benchmarks messen nichts** | ein Wächter im Harness: `CandidatesConsidered == 0` ist ein **Fehler**, nicht eine Zeile | `.TypoTolerance()` in der Fixture, der Wächter bricht den Lauf ab |
| 15 | #178 | **R1 — der Write-Benchmark auf einem lokalen Datenträger** | der Report nennt den Datenträger (heute: rot, das Feld fehlt) | `ReportWriter` schreibt den Datenträger, `tools/benchmarks.sh` legt `TMPDIR` lokal |
| 16 | #180 | **D6/D8 — die Spalte „Error" und der Lauf im Report** | der Report nennt Datum, Iteration und Datenträger (heute: rot) | die Spaltenüberschrift wird „CI 99,9 %" und der Report nennt den Lauf |
| 17 | #177 | **D3/R5 — die Lane schreibt ihre Zahlen nirgends** | ein Wächter: die Lane schreibt eine Datei mit den `[perf]`-Zeilen in das Repository (heute: rot) | die Lane schreibt `docs/benchmarks-latest.md` (oder ein Artefakt) |
| 18 | #176 | **D4/R5 — die Szene ist messbar** | ein Wächter: jedes Budget hat eine Szene, die der Korpus hergibt | **entschieden:** die acht Budgets mit `prefixLength: 3 000 000` bekommen den Korpus, der 3 Millionen Einträge hat — **`wiki-titles.tsv` (5 167 623)**; `words.txt` hat 1 908 798 und reicht nicht. Die Grenzen werden auf der Budget-Maschine neu gemessen, README und `docs/benchmarks.md` folgen (ADR-0056) |
| 18b | #183 | **Das Mischen von Embedded und Linked ist definiert** | ein Test: ein Katalog mit einem Embedded- **und** einem Linked-Index ⇒ beide antworten, und die Datei ist **eine** (gemessen: 15 Tabellen in einer Datei, darunter die Anwendungstabelle `notes`) | der Test wird Teil des Baums; die Doku sagt: *ein Katalog ist eine Datei* — die Anwendungstabelle liegt neben den Tabellen von LUI, und die Datei steht im WAL-Modus, **wenn LUI sie angelegt hat** |
| 19 | #175 | **D5 — das Zwei-Fehler-Wörterbuch bei 100 % der Grenze** | der Wächter: die Grenze in `budgets.json`, in der README und in `docs/benchmarks.md` sind **gleich** (heute: 240 MB überall, aber 100 % des Limits ⇒ keine Reserve) | **entschieden:** die Wörter bleiben, die Grenze geht von **240 MB auf 260 MB** (+8,3 % Reserve) — in `budgets.json`, `README.md:129`, `docs/benchmarks.md:31`, `:42`, `:60`; der gemessene Wert (240 MB) bleibt in `docs/typo-dictionary.md` stehen |

**Nicht in diesem PR:** die Sitzung mit einem Beutel im Katalog (gemessen: sie bringt **20,7 µs** und **~2,2 KB** je
Anfrage — dafür ist der eigene Pool, die Lebensdauer und ein Lock zu viel); die Änderungen an der Priorisierung der
Treffer (die Kosten der Korrektur auf der CI-Maschine), die Neuordnung der Hit-Anweisung, alles, was die *Semantik* der
Treffer ändert.

## Der Nachweis am Ende

```
dotnet build -c Release                                   (TreatWarningsAsErrors)
dotnet test tests/Nordmann.LUI.Unit.Tests
dotnet test tests/Nordmann.LUI.Integration.Tests
dotnet test tests/Nordmann.LUI.Architecture.Tests
dotnet test tests/Nordmann.LUI.Performance.Tests --filter "FullyQualifiedName~BudgetPerformance"
dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter '*' --job short
```

**Und die Zahlen vor und nach** (derselbe Korpus, dieselbe Maschine, im Bericht mit Datum und Iteration):
die Latenz und die Allokation des Lesepfads, der Präfix-Pfad, die Korrektur-Benchmarks, die Write-Benchmarks auf
dem lokalen Datenträger.

## Was der Owner entscheiden muss, bevor der PR entsteht

1. **D5** — ~~die Grenze des Zwei-Fehler-Wörterbuchs~~ **entschieden (2026-11-14):** die Wörter bleiben, die Grenze
   geht von 240 MB auf **260 MB** (Commit 19).
2. **R5/D4** — die Szene: **entschieden (2026-11-14):** die Wörter, die jetzt da sind, bleiben — und die acht
   Budgets mit 3 000 000 Einträgen bekommen den **Korpus, der 3 Millionen hat** (`wiki-titles.tsv`, 5 167 623);
   `words.txt` hat 1 908 798. Die Grenzen werden neu gemessen. Commit 18.
3. **Der Journal-Modus** — **entschieden (2026-11-14):** LUI startet SQLite **immer** im WAL-Modus, es gibt keinen
   Parameter und nichts überschreibt es. `WriteAheadLog` **verschwindet** aus dem API (ADR-0067).
4. **Die Form des Verbindungsobjekts** — (P1) der Verbindungsstring bleibt und die Pragmas kommen als Typ dazu
   (Empfehlung, mit der Messung: der Provider setzt keines der vier Pragmas von LUI ⇒ keine Kollision), (P2) ein
   typisiertes `LuiConnection` ersetzt den Verbindungsstring, (P3) alles so lassen und nur die fehlenden Pragmas
   ergänzen.
5. **Die Form der Sitzung** — **entschieden (2026-11-14): es gibt keine.** Der Lesepfad öffnet eine read-only
   Verbindung aus dem Pool des Providers und bereitet die Anweisungen vor; der Pool des Providers ist der Pool.
   (Gemessen: der eigene Beutel brächte 20,7 µs und ~2,2 KB je Anfrage.)
