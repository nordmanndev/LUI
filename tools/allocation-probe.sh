#!/usr/bin/env bash
# The driver of the probe of the measured window (issue #134).
#
# The allocation of the measure landed inside a measured window once, in one run, out of many:
# the run of 43228720565 said that 1912 bytes had been allocated inside the window of
# The_measure_allocates_nothing, and the hunt for them is still open. The probe of the unit
# lane watches a hundred windows at a time and writes the delta of every iteration down; this
# script dispatches that lane, waits for it, collects the reports and says how many runs there
# were. The number of the runs is part of the evidence — a clean run without a number says
# nothing.
#
#   tools/allocation-probe.sh --local                 # one run here, on this machine
#   tools/allocation-probe.sh --runs 20               # twenty runs on the runner
#   tools/allocation-probe.sh --runs 20 --parallel 4  # four of them at a time
#
# The runs happen on temporary branches that carry the commit of the branch of the probe, and
# they go away again — whether the script ends well or badly. They cannot run on the branch
# itself: the CI cancels a run of the same ref (the group of its concurrency is the ref), and
# the run of the pull request is not the run of the probe.
set -euo pipefail

readonly ARTIFACT='allocation-probe'
readonly REPORT='allocation-probe.txt'
readonly WORKFLOW='ci.yml'

runs=10
parallel=4
ref=''
local_run=0
refs=()

usage() {
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print }' "$0"
}

cleanup() {
  local name
  for name in "${refs[@]:-}"; do
    [ -n "${name}" ] || continue
    git push origin --delete "${name}" >/dev/null 2>&1 || true
  done
}

while [ $# -gt 0 ]; do
  case "$1" in
    --local) local_run=1; shift ;;
    --runs) runs="${2:-}"; shift 2 ;;
    --parallel) parallel="${2:-}"; shift 2 ;;
    --ref) ref="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if ! [ "${runs}" -gt 0 ] 2>/dev/null; then
  echo "--runs wants a number of runs" >&2
  exit 2
fi

if ! [ "${parallel}" -gt 0 ] 2>/dev/null; then
  echo "--parallel wants a number of runs at a time" >&2
  exit 2
fi

root="$(git rev-parse --show-toplevel)"
cd "${root}"

if [ "${local_run}" -eq 1 ]; then
  echo '=== the probe, here, on this machine ==='
  LUI_ALLOCATION_PROBE=1 dotnet test tests/Nordmann.LUI.Unit.Tests --nologo \
    --filter 'FullyQualifiedName~DamerauAllocationTests'
  echo '=== the report ==='
  cat "artifacts/test-results/${REPORT}"
  exit 0
fi

sha="$(git rev-parse HEAD)"
if [ -z "${ref}" ]; then
  ref="$(git rev-parse --abbrev-ref HEAD)"
fi

if ! git ls-remote --exit-code --heads origin "${ref}" >/dev/null 2>&1; then
  echo "the branch ${ref} is not on the remote — the probe has to be pushed before it runs" >&2
  exit 1
fi

trap cleanup EXIT

mkdir -p artifacts/probe/ids

echo "=== the probe of ${ref} (${sha}) ==="

# The temporary branches: one run of each of them at a time, the runs of a wave beside each
# other. A wave waits for its runs, so that the next wave may use the same branches again.
# The name of a branch carries the time of this run of the script: the runs of a branch name
# are the runs of one invocation, and the collection reads them from there.
stamp="$(date +%s)"
for i in $(seq 1 "${parallel}"); do
  name="lui-probe/${sha:0:7}-${stamp}-${i}"
  git push origin --delete "${name}" >/dev/null 2>&1 || true
  git push origin "${sha}:refs/heads/${name}" >/dev/null
  refs+=("${name}")
done

wave=0
dispatched=0
while [ "${dispatched}" -lt "${runs}" ] && [ "${wave}" -lt "$(( runs + parallel ))" ]; do
  wave=$((wave + 1))
  echo "--- wave ${wave} ---"

  pids=()
  for i in $(seq 1 "${parallel}"); do
    [ "${dispatched}" -lt "${runs}" ] || break
    dispatched=$((dispatched + 1))
    target="${refs[$((i - 1))]}"

    (
      # The run of this dispatch — and not the one of the wave before it, which ran on the
      # same branch. The id of the last run is the mark: the clock of this machine and the
      # clock of GitHub do not have to agree, and a run that was dispatched a second before
      # the dispatch of this one is older than the mark and not the run we wait for.
      previous="$(gh run list --workflow "${WORKFLOW}" --branch "${target}" --event workflow_dispatch \
        --limit 1 --json databaseId --jq '.[0].databaseId // empty')"

      gh workflow run "${WORKFLOW}" --ref "${target}" -f allocation_probe=1 >/dev/null

      id=''
      for _ in $(seq 1 60); do
        newest="$(gh run list --workflow "${WORKFLOW}" --branch "${target}" --event workflow_dispatch \
          --limit 1 --json databaseId --jq '.[0].databaseId // empty')"
        if [ -n "${newest}" ] && [ "${newest}" != "${previous}" ]; then
          id="${newest}"
          break
        fi
        sleep 5
      done

      if [ -z "${id}" ]; then
        echo "no run appeared for ${target}" >&2
        exit 1
      fi

      gh run watch "${id}" >/dev/null 2>&1 || true
      echo "${id}"
    ) >"artifacts/probe/ids/${target##*/}.txt" &
    pids+=("$!")
  done

  for pid in "${pids[@]}"; do
    wait "${pid}" || echo "a run of the wave ${wave} failed — its report is missing" >&2
  done
done

echo '=== the runs ==='
dirty=0
missing=0
count=0
for target in "${refs[@]}"; do
  mapfile -t ids < <(gh run list --workflow "${WORKFLOW}" --branch "${target}" --event workflow_dispatch \
    --json databaseId --jq '.[].databaseId')

  for id in "${ids[@]}"; do
    count=$((count + 1))
    echo "--- run ${id} ---"
    gh run view "${id}" --json conclusion --jq '.conclusion'

    dir="artifacts/probe/${id}"
    mkdir -p "${dir}"
    if ! gh run download "${id}" --name "${ARTIFACT}" --dir "${dir}" >/dev/null 2>&1 \
        || [ ! -f "${dir}/${REPORT}" ]; then
      echo "the report of the run ${id} is missing — a run without a report proves nothing" >&2
      missing=$((missing + 1))
      continue
    fi

    cat "${dir}/${REPORT}"
    windows="$(grep -c '^window ' "${dir}/${REPORT}" || true)"
    dirty=$((dirty + windows))
  done
done

echo '=== the number of the runs ==='
echo "${count} run(s), ${dirty} dirty window(s), ${missing} run(s) without a report"
