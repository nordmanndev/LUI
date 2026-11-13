#!/usr/bin/env bash
#
# benchmarks.sh — run BenchmarkDotNet, keep its reports, and bring the record in step (ADR-0022).
#
#   tools/benchmarks.sh              every benchmark, BenchmarkDotNet's default job
#   tools/benchmarks.sh --job short  a quick look — do not commit what it gives
#
# The reports land in benchmarks/results/ (BenchmarkDotNet's *-report-github.md, nothing else); the tables of
# docs/benchmarks.md are rendered from them by the architecture test ReadmeBenchmarksTests, which also fails when the two
# differ. Run it on a quiet machine: nothing else compiling, nothing else measuring. It takes about forty minutes.
#
# The corpora are files in the repository (ADR-0059) — the script fetches nothing.
#
# On a network drive BenchmarkDotNet can lose the assembly it generated for the job in the middle of the run
# ("Did not find ...-DefaultJob-1.dll"). Copy the tree to a local disk and run it there.
#
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ARTIFACTS="$(mktemp -d)"
readonly RESULTS="${ROOT}/benchmarks/results"

cleanup() { rm -rf "${ARTIFACTS}"; }
trap cleanup EXIT

dotnet run --configuration Release --project "${ROOT}/benchmarks/Nordmann.LUI.Benchmarks" -- \
  --filter '*' --exporters github --artifacts "${ARTIFACTS}" "$@"

mkdir -p "${RESULTS}"
rm -f "${RESULTS}"/*-report-github.md
cp "${ARTIFACTS}"/results/*-report-github.md "${RESULTS}/"

LUI_UPDATE_README=1 dotnet test "${ROOT}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo
dotnet test "${ROOT}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo

echo "benchmarks: the reports are in benchmarks/results/, docs/benchmarks.md is in step — review the diff and commit both"
