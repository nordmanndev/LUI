# Issue #56 — Der Read-Pfad und der Katalog als Einstiegspunkt

- **Branch:** `feature/read-path-open` (PR #57, gegen `main`)
- **ADR:** [0033 — The read path opens an index without writing](../adr/0033-the-read-path-opens-an-index-without-writing.md)
- **Issue:** #56
- **Stand:** 2026-11-05, nach der Entscheidung des Owners über die Form der API

## Das Ziel

LUI bekommt einen Read-Pfad, der die Datei **nicht schreibt**, und eine API, die so wenig wie möglich
verlangt:

1. Der **Katalog ist der Einstiegspunkt** — er wird injiziert, er ist das einzige im Container.
2. Das **Erstellen** ist ein eigener Aufruf (`Create`) und liegt in der Migration der Anwendung.
3. Im Betrieb wird der Index **geöffnet** (`Index`) — die restlichen APIs bleiben am Index.
4. **Ein Interface je Fähigkeit**, keine zweite Implementierung: die untypisierte Fassade
   (`ILuiDocumentIndex`) entfällt.
5. Die Datei entscheidet, **was erlaubt ist**: eine nur lesbare Verbindung → der Katalog schreibt nicht.

## Die API, die entsteht

```csharp
// Der Katalog — statische Fabrik, unverändert.
public static LuiCatalog Open(string connectionString, LuiCatalogOptions? options = null);

// Das Erstellen — einmal, in der Migration.
public ILuiEmbeddedIndex<TKey, TPayload> Create<TKey, TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder<TKey, TPayload>> index);

public ILuiLinkedIndex<TPayload> Create<TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index);

// Im Betrieb — der Index wird geöffnet, die Datei bleibt unberührt.
public ILuiEmbeddedIndex<TKey, TPayload> Index<TKey, TPayload>(
    string name,
    ILuiPayloadSerializer<TPayload>? payload = null);

public ILuiLinkedIndex<TPayload> Index<TPayload>(
    string name,
    Func<LuiPayloadRow, TPayload> map);

public ILuiLinkedIndex<LuiNoPayload> Index(string name);
```

`catalog.Index(name)` (der Builder) **entfällt** — damit kann der Laufzeitpfad keinen Index mehr
erstellen. `catalog.OpenIndex(...)` **entfällt** — `Open` ist die statische Fabrik des Katalogs,
`Index` ist der Index. `ILuiDocumentIndex`, `LuiDocumentHit`, `DocumentIndex`, `DocumentSource`,
`RawJsonPayload` und `ReadOnlyEmbeddedIndex` **entfallen**.

### Die nur lesbare Datei

```csharp
// Der Schreiber
var catalog = LuiCatalog.Open("Data Source=app.db");
// Der Leser — derselbe Anwendungscode
var catalog = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");
```

Ein Katalog auf einer nur lesbaren Datei:

- schaltet **nicht** auf WAL um,
- legt **`lui_meta` nicht an** — die Tabelle muss da sein; fehlt sie, sagt der Katalog das,
- verweigert **jeden Schreibvorgang** mit `LuiReadOnlyException` (neu, erbt von `LuiException`):
  `Create`, `DropIndex`, `Upsert`, `Sync`.

Die Datei entscheidet das, nicht ein Schalter in `LuiCatalogOptions` — und nicht der Index.

### Die Definition merkt sich den Payload-Typ

`IndexDefinition` bekommt `PayloadType` (der volle Name des Typs, `null` ohne Payload). Beim Erstellen
wird er aus dem Typparameter des Builders geschrieben, beim Öffnen verglichen:

```
LuiDefinitionMismatchException: the index stores the payload type 'TagChip',
you asked for 'RecipeChip'
```

Der **Serialisierer** wird nicht gespeichert — er ist ein Objekt der Anwendung. Die Anwendung übergibt
ihn beim Öffnen.

### Der Container

```csharp
services.AddLui("Data Source=app.db");
```

`AddIndex`, `AddLinkedIndex`, `AddOpenIndex`, `EnsureOnStart` und `WarmUpTypoDictionariesOnStart`
**entfallen** — samt `LuiRegistrations`, `LuiStartup`, `IndexRegistration`,
`LinkedIndexRegistration`, `OpenIndexRegistration`. `AddLui` liefert `IServiceCollection` zurück.

## Die Arbeit, Schritt für Schritt

### A. Die Bibliothek

1. `LuiCatalog`:
   - `Index(string)` (der Builder) entfernen; `Create(...)` mit den beiden Überladungen nach
     `LuiIndexKindBuilder`/`LuiEmbeddedIndexBuilder`/`LuiLinkedIndexBuilder` einführen.
   - `OpenIndex(...)` durch `Index(...)` ersetzen: die Definition aus `lui_meta` lesen, den Index daraus
     bauen, nichts schreiben. Für `Linked` die Spalten aus der Definition und die Abbildung der
     Anwendung nehmen.
   - `DropIndex` bleibt.
2. `Database`: `ReadOnly` aus der Verbindungszeichenkette ableiten
   (`new SqliteConnectionStringBuilder(connectionString).Mode == SqliteOpenMode.ReadOnly`).
   Der Writer wirft in einem nur lesbaren Katalog `LuiReadOnlyException`.
3. `LuiCatalog.Open`: bei `ReadOnly` kein WAL und kein `CREATE TABLE lui_meta`; fehlt die Tabelle,
   kommt ein klarer Fehler.
4. `IndexDefinition`: `PayloadType` ergänzen; beim Erstellen schreiben, beim Öffnen prüfen.
5. Die Dateien der Fassade löschen (siehe oben) und alle Verweise auflösen.

### B. Die Aufrufer

- `catalog.Index("x")....Create()` → `catalog.Create("x", index => index....)`. **Achtung:** der
  Lambda-Parameter heißt `index`, das Ergebnis darf nicht `index` heißen (CS0136) — z. B.
  `var recipes = catalog.Create("recipes", index => index.MultiWord().WithPayload<Recipe>(ctx));`
- `catalog.OpenIndex<TKey, TPayload>(name, payload)` → `catalog.Index<TKey, TPayload>(name, payload)`.
- `catalog.OpenIndex(name)` → entfällt.
- Der Container: `AddIndex`/`AddLinkedIndex`/`AddOpenIndex` und ihre Aufrufer (Probe, Tests) auf
  `catalog.Create(...)`/`catalog.Index(...)` umstellen.

### C. Die Probe

Die Endpunkte der Probe injizieren heute die Indizes aus dem Container. Sie injizieren jetzt **den
Katalog** und holen sich den Index bei Bedarf:

```csharp
var index = catalog.Index<long, RecipeChip>("recipes", RecipeJsonContext.Default.RecipeChip);
```

Die Probe erstellt ihre Indizes selbst (sie füllt sie auf Zuruf) — `catalog.Create(...)` in der
Probe, nicht im Container.

### D. Die Tests

- `tests/Nordmann.LUI.Integration.Tests/ReadPathTests.cs` — anpassen: die Fassade fällt weg, der
  typisierte Pfad bleibt, neu sind der nur lesbare Katalog und die Prüfung des Payload-Typs.
- `tests/Nordmann.LUI.Integration.Tests/OpenIndexHostingTests.cs` — fällt weg (der Container kennt
  keine Indizes mehr).
- Neu: `tests/Nordmann.LUI.Integration.Tests/ReadOnlyCatalogTests.cs` — der Katalog auf einer nur
  lesbaren Datei: `Open` schreibt nicht, `Upsert`/`Create`/`DropIndex`/`Sync` werfen
  `LuiReadOnlyException`, `Search` funktioniert.
- `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` — die neuen Mitglieder, die
  entfallenen.
- `docs/testing.md` — die Testkarte: jede neue Testdatei braucht eine Zeile, jede entfallene ihre
  Zeile weg (der Wächter `Test_Landscape` prüft das).

### E. Die Dokumente

- **Spezifikation** §7.1, §12, §14, §15: `Create`, `Index`, die nur lesbare Datei, die
  `PayloadType` in der Definition, der Container ohne Indizes.
- **README**: der Schnellstart (`AddLui`, `Create`, `Index`).
- **CHANGELOG**: die Migrationsliste (was von `Ensure`/`OpenIndex`/`AddIndex` nach was).
- **ADR-0033** ist geschrieben (liegt im Branch) — die Zeile in `docs/adr/README.md` anpassen, wenn
  sich der Titel ändert.

## Die Regeln des Repos

- `AGENTS.md` lesen. Ein Typ pro Datei, Methoden höchstens 60 Zeilen, keine Regex ohne Timeout,
  kultur-sichere Formatierung (Meziantou.Analyzer), `string.Equals(…, StringComparison.Ordinal)`.
- `TreatWarningsAsErrors` ist an: der Build muss **0 Warnungen** ergeben.
- Ein ADR ist nur nötig, wenn `Directory.Build.props`, `Directory.Packages.props` oder die Abhängigkeiten
  von `Nordmann.LUI`/`Nordmann.LUI.Hosting` angefasst werden — das ist hier **nicht** der Fall (ADR-0033
  existiert bereits).
- Keine neue Abhängigkeit.

## Die Annahmen, an denen die Arbeit gemessen wird

1. `catalog.Create("tags", index => index.SingleWord().WithPayload<TagChip>(ctx))` erzeugt den Index und
   liefert `ILuiEmbeddedIndex<long, TagChip>`; ein zweiter Aufruf mit derselben Beschreibung ändert
   nichts; ein Index mit einer anderen Definition ist ein `LuiDefinitionMismatchException`.
2. `catalog.Index<long, TagChip>("tags", ctx)` öffnet einen vorhandenen Index, **schreibt nichts** — der
   rote Beweis: mit offenem Write-Transaktion kehrt `Index` sofort zurück, `Create` blockt.
3. `catalog.Index<long, TagChip>("tags", ctx)` auf einem Index ohne diesen Payload-Typ ist ein
   `LuiDefinitionMismatchException`, der den Typ nennt.
4. `catalog.Index<long, RecipeChip>("tags", ctx)` auf einem Index mit `string`-Schlüssel ist ein
   `LuiDefinitionMismatchException`, der den Schlüsseltyp nennt.
5. `LuiCatalog.Open("Data Source=x.db;Mode=ReadOnly")` öffnet eine nur lesbare Datei: kein WAL, kein
   `CREATE TABLE`, `Search` funktioniert, `Upsert`/`Create`/`DropIndex`/`Sync` werfen
   `LuiReadOnlyException`.
6. `AddIndex`, `AddLinkedIndex`, `AddOpenIndex`, `ILuiDocumentIndex` und `LuiCatalog.OpenIndex` gibt es
   nicht mehr — im öffentlichen API und im Container.
7. Der Container kennt nur den Katalog: `services.AddLui("Data Source=app.db")` und sonst nichts.

## Die Abnahme

- `dotnet build -c Release`: 0 Warnungen, 0 Fehler.
- `dotnet test`: alles grün, die Zahl der Tests im Bericht.
- `dotnet pack -c Release` und `npm test` in `samples/Lui.Sample`: grün.
- Kein Force-Push, `main` bleibt unberührt, kein Merge.
