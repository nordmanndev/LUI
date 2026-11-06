#!/usr/bin/env bash
#
# benchmarks.sh — run BenchmarkDotNet, keep its reports, and bring the record in step (ADR-0022).
#
#   tools/benchmarks.sh              every benchmark, BenchmarkDotNet's default job
#   tools/benchmarks.sh --job short  a quick look — do not commit what it gives
#
# The reports land in benchmarks/results/ (BenchmarkDotNet's *-report-github.md, nothing else); the tables of
# docs/benchmarks.md are rendered from them by the architecture test Readme_Benchmarks, which also fails when the two
# differ. Run it on a quiet machine: nothing else compiling, nothing else measuring. It takes about forty minutes.
#
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ARTIFACTS="$(mktemp -d)"
readonly RESULTS="${ROOT}/benchmarks/results"

cleanup() { rm -rf "${ARTIFACTS}"; }
trap cleanup EXIT

if [ ! -f "${ROOT}/artifacts/wordlist/german-words.txt" ]; then
  echo "benchmarks: the full German word list is missing — run tools/fetch-wordlist.sh first" >&2
  echo "benchmarks: (the benchmarks would measure the small sample, not the corpus of the record)" >&2
  exit 1
fi

dotnet run --configuration Release --project "${ROOT}/benchmarks/Nordmann.LUI.Benchmarks" -- \
  --filter '*' --exporters github --artifacts "${ARTIFACTS}" "$@"

mkdir -p "${RESULTS}"
rm -f "${RESULTS}"/*-report-github.md
cp "${ARTIFACTS}"/results/*-report-github.md "${RESULTS}/"

LUI_UPDATE_README=1 dotnet test "${ROOT}/tests/Nordmann.LUI.Architecture.Tests" --filter Readme_Benchmarks --nologo
dotnet test "${ROOT}/tests/Nordmann.LUI.Architecture.Tests" --filter Readme_Benchmarks --nologo

echo "benchmarks: the reports are in benchmarks/results/, docs/benchmarks.md is in step — review the diff and commit both"
