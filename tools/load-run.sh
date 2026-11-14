#!/usr/bin/env bash
#
# load-run.sh — the run under load that keeps the whole output and names the failing test (issue #154).
#
# An integration test failed once under load, and the name of it was lost with the output of that run: the
# output was filtered, and what was left of it was the count (Failed: 1, Passed: 421, Skipped: 1,
# Total: 423). The failure came in a run where all four layers ran at once; the runs of the layer alone
# were green (issue #154).
#
# So the procedure of the hunt is this one — the evidence, not the guess:
#
#   - the four lanes of the suite run at once, because the load of that day was a machine full of test
#     processes, and the performance layer runs beside them, because a build of the benchmarks is what
#     took the machine that day;
#   - the whole output of every lane is kept, and the result of it is kept as the trx of the lane: the
#     file itself, not a grep over the output;
#   - a lane that fails names the test that failed, the moment it fails: the name and the message of it.
#
#   tools/load-run.sh                  # ten runs, the four lanes at once, the performance layer beside them
#   tools/load-run.sh --runs 30        # thirty runs
#   tools/load-run.sh --serial         # the lanes one after the other (the control)
#   tools/load-run.sh --load none      # without the load beside them (the other control)
#   tools/load-run.sh --name <trx>     # the failing tests of a trx, and nothing else
#   tools/load-run.sh --self-test      # the naming of a failing test is proved — the guard of this tool
#
# The number of the runs is part of the evidence: a run without a number says nothing. What is kept is in
# artifacts/load-run/<stamp>/ — the log of every lane, the trx of every lane and the log of the load.
#
set -uo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The four lanes of the pyramid (ADR-0019): the rules of the library without a database, the mechanical
# rules of the repository, the library against a real SQLite file, and the sample against its API.
readonly LANES='unit architecture integration sample'

# The load beside the runs: the measurement layer. It builds the indexes of the corpora and the typo
# dictionary — the work that takes the disk and the memory of a machine. The scenario of the run is the
# one of the repository (ADR-0058), so the load is the same work on every machine.
readonly DEFAULT_LOAD='dotnet test tests/Nordmann.LUI.Performance.Tests --configuration Release --no-build --nologo --logger "console;verbosity=detailed"'

runs=10
load="${DEFAULT_LOAD}"
serial=0
self_test=0
name_only=''
folder=''

usage() {
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print }' "$0"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --runs) runs="${2:-}"; shift 2 ;;
    --runs=*) runs="${1#--runs=}"; shift ;;
    --load) load="${2:-}"; shift 2 ;;
    --load=*) load="${1#--load=}"; shift ;;
    --serial) serial=1; shift ;;
    --self-test) self_test=1; shift ;;
    --name) name_only="${2:-}"; shift 2 ;;
    --name=*) name_only="${1#--name=}"; shift ;;
    --folder) folder="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if ! [ "${runs}" -gt 0 ] 2>/dev/null; then
  echo "--runs wants a number of runs" >&2
  exit 2
fi

cd "${ROOT}"

# ---------------------------------------------------------------------------------------------
# The naming: the failing tests of a trx, with their message.
#
# The trx of the runner holds the fully qualified name of the test (the testName of the
# UnitTestResult) and its message (the Message of the ErrorInfo). A run that reproduces a failure has
# to name the test — everything else is a guess (issue #154).
# ---------------------------------------------------------------------------------------------
name_failures() {
  local trx="$1"

  if [ ! -f "${trx}" ]; then
    echo "the trx ${trx} is missing — a run without a result proves nothing" >&2
    return 1
  fi

  awk '
    # The definition of a test carries its name; the result carries the outcome. The result comes
    # first in the file, so the name of the definition is the fallback, not the first answer.
    /<UnitTest / {
      id = ""
      nm = ""
      if (match($0, /id="[^"]*"/)) id = substr($0, RSTART + 4, RLENGTH - 5)
      if (match($0, /name="[^"]*"/)) nm = substr($0, RSTART + 6, RLENGTH - 7)
      if (id != "" && nm != "") full[id] = nm
      next
    }

    /<UnitTestResult / {
      failed = ($0 ~ /outcome="Failed"/)
      msg = ""
      if (failed) {
        testid = ""
        testname = ""
        if (match($0, /testId="[^"]*"/)) testid = substr($0, RSTART + 8, RLENGTH - 9)
        if (match($0, /testName="[^"]*"/)) testname = substr($0, RSTART + 10, RLENGTH - 11)
      }
      next
    }

    # The message of the failure: it may run over several lines.
    in_message {
      if ($0 ~ /<\/Message>/) {
        line = $0
        sub(/<\/Message>.*/, "", line)
        msg = msg line "\n"
        in_message = 0
        next
      }
      msg = msg $0 "\n"
      next
    }

    /<Message>/ {
      line = $0
      sub(/^.*<Message>/, "", line)
      if (line ~ /<\/Message>/) {
        sub(/<\/Message>.*/, "", line)
        msg = msg line "\n"
        next
      }
      msg = msg line "\n"
      in_message = 1
      next
    }

    /<\/UnitTestResult>/ {
      if (failed) {
        count++
        result_id[count] = testid
        result_name[count] = testname
        result_msg[count] = msg
      }
      failed = 0
      next
    }

    END {
      for (i = 1; i <= count; i++) {
        nm = result_name[i]
        if (nm == "" && (result_id[i] in full)) nm = full[result_id[i]]
        printf("the failing test: %s\n", nm)
        if (result_msg[i] != "") printf("  %s", result_msg[i])
      }
    }
  ' "${trx}"
}

# ---------------------------------------------------------------------------------------------
# The self-test: the naming of a failing test is proved. The fixture has the shape that the runner
# writes — the UnitTestResult with the outcome and the testName, the ErrorInfo with the Message.
# ---------------------------------------------------------------------------------------------
readonly FIXTURE_TEST='Nordmann.LUI.Integration.Tests.Catalog.A_test_that_failed'

self_test() {
  local work
  work="$(mktemp -d)"
  local trx="${work}/fixture.trx"

  cat > "${trx}" <<XML
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="7d2f2b1e-0f9c-4a1e-9a4a-6f1b2c3d4e5f" name="lui-load-run self-test" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="b1e2c3d4-5f6a-4b7c-8d9e-0f1a2b3c4d5e" testId="c2d3e4f5-6a7b-4c8d-9e0f-1a2b3c4d5e6f" testName="${FIXTURE_TEST}" computerName="fixture" duration="00:00:00.1000000" testType="13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b" outcome="Failed" testListId="8c84fa94-04c1-424b-9868-57a2d4851a1d">
      <Output>
        <ErrorInfo>
          <Message>the message of the failing test</Message>
          <StackTrace>   at Nordmann.LUI.Integration.Tests.Catalog.A_test_that_failed()</StackTrace>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
  </Results>
  <TestDefinitions>
    <UnitTest name="${FIXTURE_TEST}" storage="lui.integration.tests.dll" id="c2d3e4f5-6a7b-4c8d-9e0f-1a2b3c4d5e6f">
      <Execution id="b1e2c3d4-5f6a-4b7c-8d9e-0f1a2b3c4d5e" />
      <TestMethod codeBase="lui.integration.tests.dll" adapterTypeName="executor://xunit/VsTestRunner3/netcore/" className="Nordmann.LUI.Integration.Tests.Catalog" name="A_test_that_failed" />
    </UnitTest>
  </TestDefinitions>
</TestRun>
XML

  local output
  output="$(name_failures "${trx}")"
  echo "${output}"

  if echo "${output}" | grep -qF "${FIXTURE_TEST}"; then
    echo "the self-test: the failing test is named"
    return 0
  fi

  echo "the self-test FAILED: the failing test was not named — ${FIXTURE_TEST} is missing from the report" >&2
  return 1
}

# ---------------------------------------------------------------------------------------------
# The runs.
# ---------------------------------------------------------------------------------------------
lane_project() {
  case "$1" in
    unit) printf 'tests/Nordmann.LUI.Unit.Tests' ;;
    architecture) printf 'tests/Nordmann.LUI.Architecture.Tests' ;;
    integration) printf 'tests/Nordmann.LUI.Integration.Tests' ;;
    sample) printf 'samples/Lui.Sample.Tests' ;;
    *) echo "unknown lane: $1" >&2; return 1 ;;
  esac
}

# One lane of one run: the whole output goes into the log, the result into the trx, and a lane that
# fails names the test that failed — here, now, not in the summary of an hour later.
run_lane() {
  local lane="$1" index="$2"
  local number
  number="$(printf '%02d' "${index}")"
  local project
  project="$(lane_project "${lane}")" || return 1
  local log="${folder}/run-${number}-${lane}.log"
  local trx="${folder}/run-${number}-${lane}.trx"
  local failures="${folder}/run-${number}-${lane}.failures"

  dotnet test "${project}" \
    --configuration Release \
    --no-build \
    --nologo \
    --blame-hang-timeout 10m --blame-hang-dump-type full \
    --logger "trx;LogFileName=run-${number}-${lane}.trx" \
    --results-directory "${folder}" > "${log}" 2>&1
  local status=$?

  : > "${failures}"
  if [ "${status}" -eq 0 ]; then
    echo "run ${index} · ${lane}: green — $(tail -1 "${log}")"
    return 0
  fi

  echo "run ${index} · ${lane}: FAILED — the test that failed:"
  name_failures "${trx}" | tee "${failures}"
  return 1
}

# The four lanes at once, or one after the other (the control).
run_lanes() {
  local index="$1"

  if [ "${serial}" -eq 1 ]; then
    local lane
    for lane in ${LANES}; do
      run_lane "${lane}" "${index}" || true
    done
    return 0
  fi

  local pids=()
  local lane
  for lane in ${LANES}; do
    run_lane "${lane}" "${index}" &
    pids+=("$!")
  done

  local pid
  for pid in "${pids[@]}"; do
    wait "${pid}" || true
  done
}

if [ "${self_test}" -eq 1 ]; then
  self_test
  exit $?
fi

if [ -n "${name_only}" ]; then
  name_failures "${name_only}"
  exit $?
fi

# The build: the lanes run without a build of their own, so that the load beside them is the only
# thing on the machine.
echo "=== the build ==="
dotnet build Lui.sln --configuration Release --nologo || exit 1

if [ -z "${folder}" ]; then
  folder="artifacts/load-run/$(date +%Y%m%d-%H%M%S)"
fi
mkdir -p "${folder}"

# The load beside the runs. It is a process of its own, and the tool waits for it at the end — a load
# that outlives the tool would load the machine of the next thing.
load_pid=''
start_load() {
  if [ "${load}" = 'none' ]; then
    return 0
  fi
  # bash -c: the load is a command line, and its own quotes are its own.
  bash -c "${load}" >> "${folder}/load.log" 2>&1 &
  load_pid=$!
  echo "the load runs beside the runs: ${load}"
}

echo "=== the runs ==="
echo "the folder of the runs: ${folder}"

for index in $(seq 1 "${runs}"); do
  # The load is restarted when it ended: a run without a load beside it is a run of the lane alone,
  # and the runs of the layer alone were green (issue #154).
  if [ -z "${load_pid}" ]; then
    start_load
  elif ! kill -0 "${load_pid}" 2>/dev/null; then
    wait "${load_pid}" 2>/dev/null
    start_load
  fi

  echo "--- run ${index} of ${runs} ---"
  run_lanes "${index}"
done

if [ -n "${load_pid}" ]; then
  echo "=== the load ends ==="
  wait "${load_pid}" 2>/dev/null
fi

echo '=== the number of the runs ==='
failed_lanes=0
failed_runs=0
run_numbers=''
for file in "${folder}"/run-*-*.failures; do
  [ -f "${file}" ] || continue
  [ -s "${file}" ] || continue
  failed_lanes=$((failed_lanes + 1))
  name="$(basename "${file}")"
  name="${name#run-}"
  run_numbers="${run_numbers} ${name%%-*}"
done
if [ -n "${run_numbers}" ]; then
  failed_runs="$(printf '%s\n' ${run_numbers} | sort -u | wc -l | tr -d ' ')"
fi
echo "${runs} run(s) of the four lanes, ${failed_runs} run(s) with a failing test, ${failed_lanes} lane(s) that failed"

if [ "${failed_lanes}" -gt 0 ]; then
  echo '=== the tests that failed ==='
  cat "${folder}"/run-*-*.failures
  echo "the logs and the trx of every run are in ${folder}"
  exit 1
fi

echo "the logs and the trx of every run are in ${folder}"
