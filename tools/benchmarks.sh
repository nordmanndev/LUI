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
# The run happens on a LOCAL disk (issue #178). A write waits for the disk — the write benchmark on a network file system
# measured 1.85 ms where the local disk measured 0.225 ms — and on a network drive BenchmarkDotNet can lose the assembly it
# generated for the job in the middle of the run ("Did not find ...-DefaultJob-1.dll"). So the script
#   - puts TMPDIR (where the databases of the benchmarks live) on a local disk, and
#   - if the repository is on a network file system, copies the WHOLE tree to a local disk first (the run builds the
#     repository: a part of the tree is a build that breaks after two minutes with EXIT=1) — with tar, the sandbox has no
#     rsync — and leaves out what the other machine built (bin/, obj/) and .git.
# Every report names the disk it was measured on ([Disk]); a test fails on a report that was measured on a network disk.
# LUI_BENCH_LOCAL names the local directory (default: /tmp).
#
set -euo pipefail

readonly SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly LOCAL="$(mktemp -d -p "${LUI_BENCH_LOCAL:-/tmp}" lui-bench.XXXXXX)"
readonly ARTIFACTS="${LOCAL}/artifacts"
readonly RESULTS="${SOURCE}/benchmarks/results"

cleanup() { rm -rf "${LOCAL}"; }
trap cleanup EXIT

# what kind of file system is this path on? (stat -f: the file system, not the file)
is_network() {
  case "$(stat -f -c %T "$1" 2>/dev/null || true)" in
    nfs|nfs4|cifs|smb|smb2|smb3|smbfs|fuse.sshfs|ceph|afs|9p|v9fs|gfs|gfs2|lustre) return 0 ;;
    *) return 1 ;;
  esac
}

ROOT="${SOURCE}"
if is_network "${SOURCE}"; then
  ROOT="${LOCAL}/tree"
  mkdir -p "${ROOT}"
  echo "benchmarks: the repository is on a network file system — copying the whole tree to ${ROOT}"
  tar -C "${SOURCE}" --exclude=./.git --exclude='bin' --exclude='obj' -cf - . | tar -C "${ROOT}" -xf -
fi

# the databases of the benchmarks are created in TMPDIR: a local disk
mkdir -p "${LOCAL}/tmp"
export TMPDIR="${LOCAL}/tmp"

mkdir -p "${ARTIFACTS}"
dotnet run --configuration Release --project "${ROOT}/benchmarks/Nordmann.LUI.Benchmarks" -- \
  --filter '*' --exporters github --artifacts "${ARTIFACTS}" "$@"

mkdir -p "${RESULTS}"
rm -f "${RESULTS}"/*-report-github.md
cp "${ARTIFACTS}"/results/*-report-github.md "${RESULTS}/"

LUI_UPDATE_README=1 dotnet test "${SOURCE}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo
dotnet test "${SOURCE}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo

echo "benchmarks: the reports are in benchmarks/results/, docs/benchmarks.md is in step — review the diff and commit both"
