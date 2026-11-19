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
# LUI_BENCH_LOCAL names the local directory (default: /tmp); the script refuses a directory that is on a network file system.
# The tests at the end run in the repository itself (they read and write docs/benchmarks.md and the reports there), after the
# measurements — the measurements are the part that waits for the disk.
#
set -euo pipefail

# assign first, then readonly: `readonly X="$(cmd)"` hides a failure of cmd from `set -e`
SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL="$(mktemp -d -p "${LUI_BENCH_LOCAL:-/tmp}" lui-bench.XXXXXX)"
readonly SOURCE LOCAL
readonly ARTIFACTS="${LOCAL}/artifacts"
readonly RESULTS="${SOURCE}/benchmarks/results"

# a failed run keeps its directory: an hour of measurements is not thrown away because the last step failed
cleanup() {
  local status=$?
  if [ "${status}" -eq 0 ]; then
    rm -rf "${LOCAL}"
  else
    echo "benchmarks: the run failed (exit ${status}); its files stay in ${LOCAL}" >&2
  fi
}
trap cleanup EXIT

# what kind of file system is this path on? (stat -f: the file system, not the file)
is_network() {
  case "$(stat -f -c %T "$1" 2>/dev/null || true)" in
    nfs|nfs3|nfs4|cifs|smb|smb2|smb3|smbfs|fuse.sshfs|fuse.glusterfs|glusterfs|ceph|afs|9p|v9fs|gfs|gfs2|lustre|davfs) return 0 ;;
    *) return 1 ;;
  esac
}

# the "local" disk has to be local: a run of an hour on a network drive is thrown away by the test afterwards
if is_network "${LOCAL}"; then
  echo "benchmarks: ${LOCAL} is on a network file system — set LUI_BENCH_LOCAL to a directory on a local disk" >&2
  exit 1
fi

ROOT="${SOURCE}"
if is_network "${SOURCE}"; then
  ROOT="${LOCAL}/tree"
  mkdir -p "${ROOT}"
  echo "benchmarks: the repository is on a network file system — copying the whole tree to ${ROOT}"
  # only the build output of the projects (./<group>/<project>/bin, obj) stays behind — not a source directory that is called bin
  tar -C "${SOURCE}" --no-wildcards-match-slash --exclude=./.git --exclude='./*/*/bin' --exclude='./*/*/obj' -cf - . | tar -C "${ROOT}" -xf -
fi

# the databases of the benchmarks are created in TMPDIR: a local disk
mkdir -p "${LOCAL}/tmp"
export TMPDIR="${LOCAL}/tmp"

mkdir -p "${ARTIFACTS}"
dotnet run --configuration Release --project "${ROOT}/benchmarks/Nordmann.LUI.Benchmarks" -- \
  --filter '*' --exporters github --artifacts "${ARTIFACTS}" "$@"

# the reports of the run, or nothing: the old ones are only replaced if there are new ones
shopt -s nullglob
reports=("${ARTIFACTS}"/results/*-report-github.md)
if [ "${#reports[@]}" -eq 0 ]; then
  echo "benchmarks: the run produced no report" >&2
  exit 1
fi
mkdir -p "${RESULTS}"
rm -f "${RESULTS}"/*-report-github.md
cp "${reports[@]}" "${RESULTS}/"

LUI_UPDATE_README=1 dotnet test "${SOURCE}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo
dotnet test "${SOURCE}/tests/Nordmann.LUI.Architecture.Tests" --filter ReadmeBenchmarksTests --nologo

echo "benchmarks: the reports are in benchmarks/results/, docs/benchmarks.md is in step — review the diff and commit both"
