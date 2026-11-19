#!/usr/bin/env bash
#
# perf-record.sh — the record of a run of the lane (issue #177): the [perf] lines of its log, with the date, the iteration
# and the disk of the run, as a markdown file.
#
#   tools/perf-record.sh <budgets.log> <benchmarks-latest.md>
#
# The step summary of a run is gone after the run: the lane writes this file, and a job of its own publishes it. A log
# without a single [perf] line is no record — the script fails, and the old record stays.
#
# The context of the run comes from the environment (the lane sets it; a run by hand may leave it out):
#   LUI_PERF_DATE     the date of the run, UTC (default: today)
#   LUI_PERF_RUN      the iteration: which run it was (default: "unnumbered")
#   LUI_PERF_COMMIT   the commit that was measured
#   LUI_PERF_REF      the ref that was measured
#   LUI_PERF_MACHINE  the machine of the scenario: 'measurement' or 'other' (ADR-0041)
#   LUI_PERF_RESULT   whether the budgets passed
#   LUI_PERF_DIR      the directory whose disk is named (default: the current one)
#
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: perf-record.sh <budgets.log> <output.md>" >&2
  exit 2
fi

readonly LOG="$1"
readonly OUT="$2"

lines="$(grep -F '[perf]' "${LOG}" | sed -e 's/^[[:space:]]*\[perf\] //' || true)"
if [ -z "${lines}" ]; then
  echo "perf-record: ${LOG} holds no [perf] line — no record" >&2
  exit 1
fi

dir="${LUI_PERF_DIR:-.}"
fs="$(stat -f -c %T "${dir}" 2>/dev/null || echo unknown)"
case "${fs}" in
  nfs|nfs3|nfs4|cifs|smb|smb2|smb3|smbfs|fuse.sshfs|fuse.glusterfs|glusterfs|ceph|afs|9p|v9fs|gfs|gfs2|lustre|davfs) kind="network" ;;
  unknown) kind="unidentified" ;;
  *) kind="local" ;;
esac

{
  echo "# The latest run of the lane"
  echo
  echo "Written by the lane of the schedule ([ADR-0041](../.agents/adr/0041-the-budgets-of-16-run-on-a-schedule.md)); not by a person."
  echo "A number of this file is the number of **this** run — its date, its iteration and its disk stand below."
  echo
  echo "| | |"
  echo "|---|---|"
  echo "| Date | ${LUI_PERF_DATE:-$(date -u +%F)} (UTC) |"
  echo "| Iteration | ${LUI_PERF_RUN:-unnumbered} |"
  echo "| Disk | ${fs}, ${kind} |"
  echo "| Machine of the scenario | ${LUI_PERF_MACHINE:-unknown} |"
  echo "| Commit | ${LUI_PERF_COMMIT:-unknown} (${LUI_PERF_REF:-unknown}) |"
  echo "| Budgets | ${LUI_PERF_RESULT:-unknown} |"
  echo
  echo '```text'
  echo "${lines}"
  echo '```'
} > "${OUT}.tmp"
mv "${OUT}.tmp" "${OUT}"
