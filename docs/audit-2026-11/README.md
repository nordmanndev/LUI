# Das Audit vom 13./14.11.2026 — der Stand des Repos `main` @ 320605e

Dieser Zweig ist ein **Archiv**, kein Arbeitszweig: er enthält die Artefakte des
Performance-Audits, auf dem die Arbeit von 0.3.0 beruht (Epic #166).

**Keine Datei des Repos wurde geändert.** Dieser Zweig ist `main` plus zwei Verzeichnisse.

## Was hier liegt

| Pfad | Was es ist |
|---|---|
| `docs/audit-2026-11/lui-performance-audit.md` | der vollständige Audit: die Budgets gegen die Messwerte, die Vergleichbarkeit, die Hotspots, die Skalierung, der Schreibpfad, die Befunde D1–D8, die Empfehlungen R1–R5 und der Nachtrag (die drei Lesepfad-Vorschläge, gemessen) |
| `docs/audit-2026-11/adr/` | die vier Aufzeichnungen **0064–0067** im Wortlaut (englisch, wie die Aufzeichnungen des Repos) |
| `docs/audit-2026-11/plan-0.3.0.md` | der Plan des Pull Requests: 19 Commits, jede Regel rot zuerst |
| `tools/audit-probes-2026-11/` | die 20 Proben (13 C#, 7 Python) — sie sind **außerhalb** des Repos gebaut worden |

## Warum ein Zweig und kein Gist

Der Token der Agenten hat **keinen** `gist`-Scope (`403: Resource not accessible by
personal access token`). Der Zweig ist der Ort, den der Eigentümer kontrolliert.

## Die Proben bauen

    export HOME=/root DOTNET_ROOT=/root/.dotnet DOTNET_CLI_HOME=/root PATH=/root/.dotnet:$PATH
    dotnet run -c Release --project tools/audit-probes-2026-11/lui_probe8

Die `ProjectReference` zeigen **relativ** auf `../../../../LUI/...` — der Checkout liegt
also zwei Ebenen über `tools/`. Zum Umbauen der Pfade:

    sed -i 's#\.\./\.\./\.\./\.\./LUI/#/pfad/zu/LUI/#g' tools/audit-probes-2026-11/*/*.csproj

Die Python-Proben laufen mit `python3 tools/audit-probes-2026-11/<name>.py`.

**Nie zwei dotnet-Kommandos gleichzeitig** — die Maschine hat 2 Kerne und 3 GB.

## Der Stand in den Issues

Der vollständige Audit steht **im Issue #168** (im Text, nicht nur als Link), die vier
Aufzeichnungen stehen **im Wortlaut** in ihren Issues, und der Plan steht **im Epic #166**.
Dieser Zweig trägt zusätzlich die **Proben**, die in kein Issue passen.
