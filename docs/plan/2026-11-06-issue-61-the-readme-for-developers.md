# Plan — Issue #61: die README für Entwickler, das Tutorial, die Karte der Dokumentation

Stand: 2026-11-06 · Branch: `docs/readme-and-tutorial` (von `main` = `111eefb`) · Issue:
[#61](https://github.com/nordmanndev/LUI/issues/61)

## Das Ziel

Die README ist heute ein Architektur-Dokument. Sie wird der Einstieg für Entwickler, die LUI benutzen
wollen — und die Lücke, die sie seit jeher hat, wird gefüllt: **ein Weg, das zu lernen**.

Die drei Vorbilder, die der Owner genannt hat, liefern die Form: *Best-README-Template* (Abzeichen,
Inhaltsverzeichnis, klarer Aufbau), *awesome-readme* (kurz, ein Beispiel, nichts Überflüssiges) und der
.NET-Blog über NuGet-READMEs (was, welches Problem, Highlights, Installation, ein laufendes Beispiel,
die wichtigsten Typen, wo es weitergeht).

## Die Diagnose, gegen `main` geprüft

| | Befund |
|---|---|
| Umfang | 571 Zeilen, 18 Abschnitte |
| Die fünf Konzeptabschnitte (182–311) | ein Duplikat von `docs/spec/LUI-Spezifikation.md` — zwei Orte für eine Wahrheit |
| Performance (ab 387) | das Vertrauensargument steht nach zwei Dritteln der Datei |
| BenchmarkDotNet-Tabellen (414–494) | 80 Zeilen Rohdaten im falschen Dokument |
| Inhaltsverzeichnis, Abzeichen | keine |
| `normalization.md`, `architecture/alignment.md`, `domain/lui.md` | **null** Verweise aus der README — verwaist |

Die API ist seit `main` = `111eefb` endgültig: der Index kennt keine Typen (ADR-0035), der Serialisierer
gehört dem Katalog, der Typ steht dort, wo der Payload benutzt wird. Die README wird gegen diese API
geschrieben, nicht gegen eine spätere.

## Die Arbeit

### 1. Das Tutorial (`docs/tutorial/`)

Die Spezifikation ist ein Vertrag, kein Wegweiser. Das Tutorial ist der Weg: von der leeren Datenbank bis
zur funktionierenden Autocomplete-Suche, in Schritten, mit lauffähigem Code nach jedem Schritt.

- `docs/tutorial/README.md` — der Wegweiser: die Schritte, was man danach kann, wie lange es dauert.
- `docs/tutorial/01-erster-index.md` — der Katalog, der erste Index, die ersten Einträge, die erste Abfrage.
- `docs/tutorial/02-eigene-daten.md` — ein Index über die eigene Tabelle (`Linked`), der Abgleich, die Trigger.
- `docs/tutorial/03-nutzdaten.md` — der Payload: der Serialisierer des Katalogs, `GetPayload<T>()`.
- `docs/tutorial/04-auf-vielen-eintraegen.md` — `Sync`, die Grenzen, was es kostet (mit den gemessenen Zahlen).
- `docs/tutorial/05-in-der-anwendung.md` — `AddLui` in ASP.NET Core, der Katalog aus dem Container, der
  Nur-Lesen-Zugang.

Regeln: jedes Codebeispiel **muss übersetzbar sein** (der Paket-Smoke-Test oder ein eigener Test
übernimmt das); „warum so?" als Randnotiz statt als Kapitel; nichts, was die Spezifikation nicht sagt.

### 2. Die neue README

Höchstens ~250 Zeilen, in dieser Reihenfolge:

1. Titel, Tagzeile, **Abzeichen** (Build, Version, NuGet, Lizenz, .NET).
2. **Was LUI ist** — drei Sätze.
3. **Das Problem** — zwei Sätze: 98 % der Eingaben sind Lesevorgänge; ein zweiter Datenspeicher für
   dieselben Daten ist der teuerste Teil.
4. **Highlights** — fünf bis sieben outcome-fokussierte Bullets.
5. **Inhaltsverzeichnis**.
6. **Install** — die zwei `dotnet add package`-Zeilen.
7. **Quick start** — **ein** Beispiel, ~20 Zeilen, mit der Ausgabe.
8. **Performance** — die Budgettabelle (Ziel / gemessen / Limit), ein Satz zur Maschine, ein Link nach
   `docs/benchmarks.md`.
9. **Die zwei Indexarten und die zwei Inhaltsquellen** — **eine** Tabelle statt zwei Abschnitte.
10. **Ein Payload** — der Serialisierer des Katalogs, `GetPayload<T>()` (ein eigener Abschnitt, damit der
    Quick start kurz bleibt).
11. **In einer ASP.NET Core-Anwendung** — `AddLui` und der Katalog.
12. **Nur lesen** — `Mode=ReadOnly`.
13. **Die wichtigsten Typen** — eine Tabelle: `LuiCatalog`, `LuiEntry`, `LuiKey`, `LuiQueryOptions`,
    `LuiCatalogOptions`.
14. **Die Beispielanwendung** — die zwei Screenshots und der Weg zum Sample.
15. **Die Dokumentation** — die Karte: Tutorial, Spezifikation, Benchmarks, Normalisierung, ADRs, Testkarte.
16. **Status, Beitragen, Lizenz** — der Repo-Teil, nach hinten.

Was rausgeht: die fünf Konzeptabschnitte (sie stehen vollständig in der Spezifikation), die
BenchmarkDotNet-Tabellen (sie gehören nach `docs/benchmarks.md`), der lange „Status"-Block (nach hinten).

**„What this is not" bleibt** — `AGENTS.md` Zeile 16 verlangt es.

### 3. Die Karte der Dokumentation

- `docs/README.md` — neu: was liegt wo, was ist der Vertrag, was ist die Historie, wer liest was.
- `docs/adr/README.md` — eine Spalte „Thema" (API / Speicher / Messung / Betrieb / Probe), damit ein
  Entwickler „was wurde entschieden" findet, ohne 36 Dateien zu öffnen.
- Die verwaisten Dokumente (`normalization.md`, `architecture/alignment.md`, `domain/lui.md`) werden aus
  der README verlinkt.
- `docs/plan/` — eine `README.md`, die die Lieferpläne als die Geschichte des Repos erklärt.

### 4. Die Wächter und das Papier

- `Readme_Benchmarks` — der Wächter vergleicht die Benchmark-Abschnitte **in der README** mit den
  BenchmarkDotNet-Reports. Wenn die Tabellen umziehen, zieht der Wächter mit: er prüft die neue Stelle
  (`docs/benchmarks.md`) und rendert dorthin.
- **ADR-0022** („The benchmark results in the README") wird neu gefasst: die Rohdaten liegen in
  `docs/benchmarks.md`, die Budgets in der README.
- Eine neue ADR für die Neuordnung (die README ist der Einstieg, die Spezifikation der Vertrag, das
  Tutorial der Weg) — in der Hausart der anderen.
- Der Paket-Smoke-Test führt jeden C#-Block der README aus — das muss nach der Neuordnung weiterhin
  gelten (und für die verschobenen Blöcke gilt: die Konzepte stehen jetzt in der Spezifikation).

### 5. Bauen, testen, committen, pushen, den Pull Request füllen

- `dotnet build -c Release`, `dotnet test`, `dotnet pack -c Release`, `npm test` — Exit 0, die echte
  Ausgabe in den Bericht und in den Pull Request.
- Ein Commit auf dem Zweig, auf Englisch, im Stil des Repos. `main` bleibt unberührt, kein Force-Push.
- Der Pull Request gegen `main` bekommt einen neuen Body: Was, Warum (Issue #61), die echte
  Verification-Ausgabe, die geänderten Tests, der CHANGELOG-Hinweis.
- Nicht auf die CI warten, nichts mergen.

### 6. Die Verifikation des Pull Requests

Read-only, mit eigenen Belegen und nicht mit den Tests des Entwicklers:

- baut und testet der Zweig ✓
- ist die README kürzer, hat sie ein Inhaltsverzeichnis und Abzeichen, steht die Performance im ersten
  Drittel, sind die Konzepte draußen ✓
- sind die verwaisten Dokumente verlinkt ✓
- funktioniert der Wächter noch (der Benchmark-Vergleich besteht) ✓
- stimmen die Codebeispiele mit dem Code überein (die README zeigt keine API, die es nicht gibt) ✓
- ist `main` unberührt, kein Force-Push, kein Build-Müll, keine Secrets im Diff ✓

Bericht nach `/workspace/verify-61-readme.md`, die erste Zeile PASS oder FAIL, die letzte Zeile der
Zusammenfassung `VERDICT: PASS` oder `VERDICT: FAIL`.

## Die Annahmen, die geprüft werden

a) Die README ist höchstens ~250 Zeilen, hat ein Inhaltsverzeichnis und Abzeichen.
b) Die fünf Konzeptabschnitte stehen in der Spezifikation und nicht mehr in der README.
c) Die Performance-Budgettabelle steht vor dem ersten Drittel der README.
d) Die BenchmarkDotNet-Rohdaten stehen in `docs/benchmarks.md`, und der Wächter `Readme_Benchmarks`
   prüft dort — der Test ist grün.
e) `docs/normalization.md`, `docs/architecture/alignment.md` und `docs/domain/lui.md` sind aus der
   README erreichbar; `docs/README.md` existiert.
f) Das Tutorial existiert, und jedes seiner Codebeispiele übersetzt.
g) Jeder C#-Block der README übersetzt und läuft im Paket-Smoke-Test.
h) Die README zeigt keine API, die der Code nicht hat (AGENTS.md).
i) `main` ist unberührt, kein Force-Push, kein Build-Müll, keine Secrets.

## Was offen ist

Nichts. Der Owner hat den Plan am 2026-11-06 freigegeben: erst das Tutorial, dann die README, dann die
Karte, alles in einem Pull Request gegen Issue #61.
