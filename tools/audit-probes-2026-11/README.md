# Die Proben des Performance-Audits (LUI, 2026-11-13/14)

Diese Proben sind **außerhalb** des Repositories gebaut worden — im Repository steht keine
Probe, und für das Audit wurde **keine Zeile** am Code geändert.

## Aufbau

    <wurzel>/
      LUI/                 <- der Checkout von github.com/nordmanndev/LUI
      tools/
        audit-probes-2026-11/
          lui_probe8/
            Program.cs
            probe.csproj

Die `ProjectReference` in den `probe.csproj` zeigen **relativ** auf `../../../../LUI/...`.

## Die Proben

| Probe | Was sie misst | Sprache |
|---|---|---|
| `lui_probe` | der erste Einstieg: der Schreibpfad, die Zeit je Eintrag | C# (LUI) |
| `lui_probe3` | die Korrektur im Präfix-Pfad (SymSpell) | C# (LUI + Testing) |
| `lui_probe4` | die Kosten der Suchform, der Wortanfang | C# (LUI + Testing) |
| `lui_probe5` | der Aufbau eines Indexes, die Kosten der Spalten | C# (LUI + Testing) |
| `lui_probe6` | `Prepare()` mit benannten Parametern | C# (Microsoft.Data.Sqlite) |
| `lui_probe7` | die Keywords des Providers (per Reflection) | C# (Microsoft.Data.Sqlite) |
| `lui_probe8` | was der Provider tut, was eine read-only Verbindung setzen darf | C# (Microsoft.Data.Sqlite) |
| `lui_probe9` | Embedded und Linked in **einer** Datei | C# (LUI) |
| `lui_probe10` | Pooling und fünf Varianten des Lesepfads | C# (Microsoft.Data.Sqlite) |
| `lui_probe11` | die Kosten des FTS5-Parse, die Seiten-Caches des Pools | C# (Microsoft.Data.Sqlite) |
| `lui_probe12` | neues Kommando je Abfrage gegen ein wiederverwendetes | C# (Microsoft.Data.Sqlite) |
| `symspell_probe` | die Kosten des Wörterbuchs | C# (LUI) |
| `probe_prefix_path.py` | der Präfix-Pfad, der Plan von SQLite (`EXPLAIN QUERY PLAN`) | Python |
| `probe_write_path.py` | der Schreibpfad, die Klassen von Schreibanfragen | Python |
| `probe_write_classes.py` | die Schreibklassen nach ihrer Wirkung | Python |
| `probe_checkpoint.py` | der Checkpoint, der WAL | Python |
| `probe_read_path.py` | der Lesepfad, die Anteile | Python |
| `probe_mmap_and_prepare.py` | `mmap_size` und `Prepare()` | Python |
| `probe_journal_mode.py` | der Journal-Modus | Python |
| `probe_journal_mode_realistic.py` | der Journal-Modus mit einem Leser, der einen Snapshot hält | Python |

## Bauen und laufen

    export HOME=/root DOTNET_ROOT=/root/.dotnet DOTNET_CLI_HOME=/root PATH=/root/.dotnet:$PATH
    dotnet run -c Release --project tools/audit-probes-2026-11/lui_probe8

Die Python-Proben laufen mit `python3 tools/audit-probes-2026-11/<name>.py`.
