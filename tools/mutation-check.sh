#!/usr/bin/env bash
#
# mutation-check.sh — prove that the guards actually bite.
#
# A green test proves nothing until it has been seen red. Every rule that the
# test suite holds up is mutated here in an isolated copy of the repository,
# and the guard must react.
#
# Two mechanisms can catch a mutation, and every mutation says which one it
# expects (issue #73):
#
#   expect_caught    the test of the filter runs and fails → the rule is a real
#                    test. A build that refuses the mutation is a FAIL here: the
#                    test was never asked, and a mutation that is caught for the
#                    wrong reason is worth nothing (pull request #69 had one).
#   expect_refused   the build refuses it with a named error → the rule is
#                    enforced by the toolchain on purpose (central package
#                    management). A test that runs, or another error, is a FAIL.
#
# Each mutation runs on its own copy: two runs against the same files destroy
# each other's evidence.
#
# The check takes a shard — `--shard 2/4` or MUTATION_SHARD=2/4 — and runs every
# fourth mutation, starting at the second. The CI runs the shards side by side;
# without a shard the script runs every mutation.
#
# The architecture rules are mutated in the architecture project, the rules of
# the library itself in the unit project — the fast one, because it needs no
# database and a mutation that takes 40 seconds is a mutation nobody runs.
# The rules of the concurrency need a database, so their mutations run in the
# integration project: they are the slow ones.
#
set -uo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly WORK="$(mktemp -d)"

readonly ARCHITECTURE_PROJECT="tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj"
readonly UNIT_PROJECT="tests/Nordmann.LUI.Unit.Tests/Nordmann.LUI.Unit.Tests.csproj"
readonly INTEGRATION_PROJECT="tests/Nordmann.LUI.Integration.Tests/Nordmann.LUI.Integration.Tests.csproj"

# The interpreter of the tools of the repository: Python (tools/link-check.py, tools/tutorial-check.py).
# The name is python3 on Linux and python on Windows — the first one that is there is the one.
readonly PYTHON="$(command -v python3 || command -v python || echo python3)"

cleanup() { rm -rf "${WORK}"; }
trap cleanup EXIT

export HOME="${HOME:-/root}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${HOME}/.dotnet-home}"
export DOTNET_NOLOGO=1

# The shard: "i/n" runs the mutations whose number modulo n is i - 1.
shard="${MUTATION_SHARD:-1/1}"
only=""
while [ $# -gt 0 ]; do
  case "$1" in
    --shard) shard="$2"; shift 2 ;;
    --shard=*) shard="${1#--shard=}"; shift ;;
    --only) only="$2"; shift 2 ;;
    --only=*) only="${1#--only=}"; shift ;;
    *) echo "usage: $0 [--shard i/n] [--only <name of a mutation>]" >&2; exit 2 ;;
  esac
done
if ! [[ "${shard}" =~ ^([1-9][0-9]*)/([1-9][0-9]*)$ ]] || [ "${BASH_REMATCH[1]}" -gt "${BASH_REMATCH[2]}" ]; then
  echo "ABORT the shard ${shard} is not i/n with 1 <= i <= n" >&2
  exit 2
fi
readonly SHARD_INDEX="${BASH_REMATCH[1]}"
readonly SHARD_COUNT="${BASH_REMATCH[2]}"
mutation_number=0
mutations_run=0

# in_shard <name> — counts every mutation and says whether this run runs it. Every expectation calls it first, so
# that the numbering is the same in every shard and every mutation runs in exactly one of them. `--only <name>` runs
# one mutation, whatever its shard — for the developer who is writing it.
in_shard() {
  mutation_number=$((mutation_number + 1))
  if [ -n "${only}" ]; then
    [ "$1" = "${only}" ] || return 1
  elif [ $(((mutation_number - 1) % SHARD_COUNT)) -ne $((SHARD_INDEX - 1)) ]; then
    return 1
  fi
  mutations_run=$((mutations_run + 1))
  return 0
}
export DOTNET_CLI_TELEMETRY_OPTOUT=1

failures=0

prepare_copy() {
  local name="$1"
  local target="${WORK}/${name}"

  rm -rf "${target}"
  mkdir -p "${target}"
  tar -C "${ROOT}" \
    --exclude=bin --exclude=obj --exclude=artifacts --exclude=.git \
    -cf - . | tar -C "${target}" -xf -

  echo "${target}"
}

# expect_caught <name> <project-file> <test-filter> <mutation-command>
expect_caught() {
  in_shard "$1" || return 0
  local name="$1"
  local project="$2"
  local filter="$3"
  local mutation="$4"
  local copy
  copy="$(prepare_copy "${name}")"

  ( cd "${copy}" && eval "${mutation}" ) >/dev/null 2>&1

  local output
  output="$( cd "${copy}" && dotnet test "${project}" \
      --filter "${filter}" --nologo 2>&1 )"

  if echo "${output}" | grep -q "No test matches the given testcase filter"; then
    # The filter names a test that does not exist — the script is broken, and
    # a broken script reports a green that proves nothing.
    echo "FAIL  ${name}: the filter matches no test — the script is broken"
    echo "${output}" | tail -6
    failures=$((failures + 1))
  elif echo "${output}" | grep -q "Failed!"; then
    echo "PASS  ${name}: CAUGHT BY TEST"
  elif echo "${output}" | grep -qE "error (NU|MSB|CS)[0-9]+"; then
    # The build refused the mutation, and the test of the filter never ran: it proves nothing about the test.
    echo "FAIL  ${name}: REFUSED BY BUILD — the mutation was meant for a test; rewrite it so that it builds"
    echo "${output}" | grep -E "error (NU|MSB|CS)[0-9]+" | head -3
    failures=$((failures + 1))
  else
    echo "FAIL  ${name}: nothing reacted to the mutation"
    echo "${output}" | tail -6
    failures=$((failures + 1))
  fi
}

# expect_refused <name> <project-file> <error-code> <reason> <mutation-command>
#
# The toolchain is the guard on purpose: the build has to refuse the mutation with exactly this error. A test that
# runs, or another error, means that the guard is not the one the script names.
expect_refused() {
  in_shard "$1" || return 0
  local name="$1"
  local project="$2"
  local code="$3"
  local mutation="$5"
  local copy
  copy="$(prepare_copy "${name}")"

  ( cd "${copy}" && eval "${mutation}" ) >/dev/null 2>&1

  local output
  output="$( cd "${copy}" && dotnet build "${project}" --nologo 2>&1 )"

  if echo "${output}" | grep -qE "error ${code}\b"; then
    echo "PASS  ${name}: REFUSED BY BUILD (${code})"
  elif echo "${output}" | grep -qE "error (NU|MSB|CS)[0-9]+"; then
    echo "FAIL  ${name}: refused by the build, but not with ${code} — the guard is not the one the script names"
    echo "${output}" | grep -E "error (NU|MSB|CS)[0-9]+" | head -3
    failures=$((failures + 1))
  else
    echo "FAIL  ${name}: the build accepted the mutation — the toolchain does not guard this rule"
    echo "${output}" | tail -6
    failures=$((failures + 1))
  fi
}

# expect_command_caught <name> <command> <mutation-command> <text-the-output-has-to-name>
#
# The guard is a command and not a test (issue #128): the tutorial check compiles the C# blocks of the
# documentation and compares what they print with the pages. The mutation is run in a copy of the
# repository, and the command has to fail — and it has to name what it found, so that a command that
# fails for another reason is not a proof.
expect_command_caught() {
  in_shard "$1" || return 0
  local name="$1"
  local command="$2"
  local mutation="$3"
  local expected="$4"
  local copy
  copy="$(prepare_copy "${name}")"

  ( cd "${copy}" && eval "${mutation}" ) >/dev/null 2>&1

  local output status
  output="$( cd "${copy}" && eval "${command}" 2>&1 )"
  status=$?

  if [ "${status}" -eq 127 ]; then
    # The command does not exist — a broken script reports a green that proves nothing.
    echo "FAIL  ${name}: the command does not exist — the script is broken"
    failures=$((failures + 1))
  elif [ "${status}" -eq 0 ]; then
    echo "FAIL  ${name}: the command accepted the mutation"
    echo "${output}" | tail -8
    failures=$((failures + 1))
  elif ! grep -qF "${expected}" <<<"${output}"; then
    # It failed, but it did not name what the mutation broke.
    echo "FAIL  ${name}: the command failed, but it did not name '${expected}'"
    echo "${output}" | tail -8
    failures=$((failures + 1))
  else
    echo "PASS  ${name}: CAUGHT BY THE COMMAND"
  fi
}

# expect_pass <name> <project-file> <test-filter>
expect_pass() {
  in_shard "$1" || return 0
  local name="$1"
  local project="$2"
  local filter="$3"
  local copy
  copy="$(prepare_copy "${name}")"

  local output
  output="$( cd "${copy}" && dotnet test "${project}" \
      --filter "${filter}" --nologo 2>&1 )"

  if echo "${output}" | grep -q "No test matches the given testcase filter"; then
    echo "FAIL  ${name}: the filter matches no test — the script is broken"
    echo "${output}" | tail -8
    failures=$((failures + 1))
  elif echo "${output}" | grep -q "Passed!"; then
    echo "PASS  ${name}: the guard is green on the clean tree"
  else
    echo "FAIL  ${name}: the guard is red on the clean tree"
    echo "${output}" | tail -8
    failures=$((failures + 1))
  fi
}

echo "=== mutation check (shard ${SHARD_INDEX}/${SHARD_COUNT}) ==="

# 0. Pre-flight: the guards are green on the clean tree. When they are not, every mutation below would "fail" for
#    the same reason — an environment that cannot run dotnet test — and the report would be ten false alarms.
preflight="$(prepare_copy "preflight")"
if ! ( cd "${preflight}" && dotnet test "${ARCHITECTURE_PROJECT}" \
    --filter "FullyQualifiedName~RepositoryLayoutTests" --nologo 2>&1 ) | grep -q "Passed!"; then
  echo "ABORT the architecture tests do not pass on the clean tree — fix the environment first"
  echo "      (dotnet on the PATH, a writable DOTNET_CLI_HOME: ${DOTNET_CLI_HOME}, packages restorable)"
  exit 2
fi

# 1. A package version sneaks into a project file. Central package management refuses it at restore time (NU1008)
#    — the toolchain is the guard; the test No_PackageReference_carries_a_Version is the second line, for a tree
#    that turns central management off.
expect_refused "package-version-in-csproj" "${ARCHITECTURE_PROJECT}" NU1008 \
  "central package management: a version belongs in Directory.Packages.props" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" Version=\"6.7.3\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 2. A package without a central version. Central package management refuses
#    this at restore time (NU1010) — the toolchain is the guard here, the test
#    Every_PackageReference_has_a_Central_PackageVersion is the second line.
expect_refused "package-without-central-version" "${ARCHITECTURE_PROJECT}" NU1010 \
  "central package management: every package has a central version" \
  "sed -i 's|<PackageReference Include=\"NetArchTest.Rules\" />|<PackageReference Include=\"NetArchTest.Rules\" /><PackageReference Include=\"Serilog\" />|' ${ARCHITECTURE_PROJECT}"

# 3. The sample becomes packable.
expect_caught "sample-is-packable" "${ARCHITECTURE_PROJECT}" \
  "Only_the_library_project_is_packable" \
  "sed -i 's|<IsPackable>false</IsPackable>|<IsPackable>true</IsPackable>|' samples/Lui.Sample/Lui.Sample.csproj"

# 4. The library reaches for another project. The stub has no reference back,
#    so the build stays valid and the test has to be the one that reacts.
expect_caught "library-references-another-project" "${ARCHITECTURE_PROJECT}" \
  "The_library_project_references_no_other_project" \
  "mkdir -p tests/Stub && printf '<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' > tests/Stub/Stub.csproj && sed -i 's|</Project>|  <ItemGroup><ProjectReference Include=\"../../tests/Stub/Stub.csproj\" /></ItemGroup></Project>|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 5. An undocumented dependency — with its central version, so the build
#    stays valid and the *test* has to be the one that reacts.
expect_caught "undocumented-dependency" "${ARCHITECTURE_PROJECT}" \
  "The_library_references_only_the_documented_dependencies" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" /><PackageReference Include=\"Serilog\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj && sed -i 's|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" />|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" /><PackageVersion Include=\"Serilog\" Version=\"4.3.1\" />|' Directory.Packages.props"

# 6. A source file in the wrong namespace.
expect_caught "namespace-folder-mismatch" "${ARCHITECTURE_PROJECT}" \
  "Every_source_file_declares_the_namespace_of_its_folder" \
  "mkdir -p src/Nordmann.LUI/Schema && printf 'namespace Nordmann.LUI.Wrong;\n\ninternal static class Probe\n{\n}\n' > src/Nordmann.LUI/Schema/Probe.cs"

# 7. The licence or the readme of the package changes.
expect_caught "package-readme-missing" "${ARCHITECTURE_PROJECT}" \
  "The_library_ships_the_readme_and_the_mit_license" \
  "sed -i 's|<PackageReadmeFile>README.md</PackageReadmeFile>|<PackageReadmeFile>Handbook.md</PackageReadmeFile>|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 8. The query path reaches up to the catalog — the named exception of ADR-0014 is gone (review DevEx D4, PR-007).
expect_caught "query-path-uses-the-catalog" "${ARCHITECTURE_PROJECT}" \
  "No_layer_goes_up_through_the_facade" \
  "printf 'namespace Nordmann.LUI.Querying;\n\ninternal static class Probe\n{\n    internal static string Name(LuiCatalog catalog) => catalog.Path;\n}\n' > src/Nordmann.LUI/Querying/Probe.cs"

# 9. The connections reach up to the schema: Storage is the bottom layer.
expect_caught "storage-knows-the-schema" "${ARCHITECTURE_PROJECT}" \
  "A_namespace_knows_only_the_ones_below_it" \
  "printf 'namespace Nordmann.LUI.Storage;\n\ninternal static class Probe\n{\n    internal static System.Type Type => typeof(Nordmann.LUI.Schema.RowId);\n}\n' > src/Nordmann.LUI/Storage/Probe.cs"

# 10. The core takes a dependency of the hosting (ADR-0018): the console application would carry it.
expect_caught "core-references-the-hosting-abstractions" "${ARCHITECTURE_PROJECT}" \
  "The_library_ships_only_the_documented_runtime_dependencies" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" /><PackageReference Include=\"Microsoft.Extensions.DependencyInjection.Abstractions\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 11. A pinned version nobody references (ADR-0019) — the pin that Alba was.
expect_caught "unused-central-package-version" "${ARCHITECTURE_PROJECT}" \
  "Every_Central_PackageVersion_is_referenced_somewhere" \
  "sed -i 's|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" />|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" /><PackageVersion Include=\"Alba\" Version=\"8.5.3\" />|' Directory.Packages.props"

# 12. A test file that the map of the tests does not list (ADR-0019).
expect_caught "test-file-missing-from-the-map" "${ARCHITECTURE_PROJECT}" \
  "Every_test_file_is_listed_in_the_map" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\npublic sealed class UnlistedTests\n{\n    [Fact]\n    public void A_test()\n    {\n    }\n}\n' > tests/Nordmann.LUI.Unit.Tests/UnlistedTests.cs"

# 13. A row of the map that points at a file which does not exist (ADR-0019).
expect_caught "map-row-without-a-file" "${ARCHITECTURE_PROJECT}" \
  "Every_file_the_map_lists_exists" \
  "sed -i 's|SimilarityTests\.cs)|SimilarityTestsGone.cs)|' docs/testing.md"

# 13b. A unit test that opens a database (ADR-0019) — the unit layer is the rules on their own.
expect_caught "unit-test-opens-a-database" "${ARCHITECTURE_PROJECT}" \
  "The_unit_layer_opens_no_database" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\ninternal static class Probe\n{\n    internal static object Open() => LuiCatalog.Open(\"probe.db\");\n}\n' > tests/Nordmann.LUI.Unit.Tests/Probe.cs"

# 13c. A test file whose only test is skipped is still a test file the map has to list (review of PR #25).
expect_caught "skipped-test-file-missing-from-the-map" "${ARCHITECTURE_PROJECT}" \
  "Every_test_file_is_listed_in_the_map" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\npublic sealed class SkippedTests\n{\n    [Fact(Skip = \"later\")]\n    public void A_test()\n    {\n    }\n}\n' > tests/Nordmann.LUI.Unit.Tests/SkippedTests.cs"

# 13d. A test file of the sample is not listed (issue #76): the tests of the sample sit beside it, outside tests/ — the
#      guard of the map has to see them there too.
expect_caught "sample-test-file-missing-from-the-map" "${ARCHITECTURE_PROJECT}" \
  "Every_test_file_is_listed_in_the_map" \
  "printf 'namespace Nordmann.LUI.Sample.Tests;\n\npublic sealed class UnlistedSampleTests\n{\n    [Fact]\n    public void A_test()\n    {\n    }\n}\n' > samples/Lui.Sample.Tests/UnlistedSampleTests.cs"

# 13e. A row of the map names a layer its file does not sit in (issue #89): the link points at the sample, the row says
#      "Integration" — the guard compared the project of the link and never the layer of the row.
expect_caught "map-row-names-another-layer" "${ARCHITECTURE_PROJECT}" \
  "Every_file_the_map_lists_sits_in_the_layer_the_map_names" \
  "sed -i 's#^\(| \[\`SampleBulkTests\`\]([^)]*) | \)Sample |#\1Integration |#' docs/testing.md"

# 13f. A test project the map has no layer for (issue #89): a fixed list of projects did not see a new one at all.
expect_caught "test-project-without-a-layer" "${ARCHITECTURE_PROJECT}" \
  "Every_test_project_of_the_tree_has_a_layer_of_the_map" \
  "mkdir -p samples/Lui.Sample.Tests2 && printf '<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>\n' > samples/Lui.Sample.Tests2/Lui.Sample.Tests2.csproj"

# 13g. A test project that does not write IsTestProject down (review of pull request 90, M1): the test SDK sets the
#      property itself, so the project is a test project all the same — and the map has to have a layer for it.
expect_caught "test-sdk-project-without-a-layer" "${ARCHITECTURE_PROJECT}" \
  "Every_test_project_of_the_tree_has_a_layer_of_the_map" \
  "mkdir -p samples/Lui.Sample.Tests3 && printf '<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>\n' > samples/Lui.Sample.Tests3/Lui.Sample.Tests3.csproj"

# 13h. A test whose namespace is not its folder (issue #77): the folders of the tests are their subjects, as in the library.
expect_caught "test-file-in-another-namespace" "${ARCHITECTURE_PROJECT}" \
  "Every_test_source_file_declares_the_namespace_of_its_folder" \
  "printf 'namespace Nordmann.LUI.Integration.Tests;\n\ninternal static class Probe\n{\n}\n' > tests/Nordmann.LUI.Integration.Tests/Linked/Probe.cs"

# 14. The lazy similarity of ADR-0008 — the ranking measures every candidate
#     again, and the promise that a group that cannot reach the hits costs
#     nothing is gone.
expect_caught "the-lazy-similarity-is-gone" "${UNIT_PROJECT}" \
  "A_group_that_cannot_reach_the_hits_never_has_its_similarity_computed" \
  "sed -i 's|for (var g = 0; g < GroupCount \&\& result.Count < take; g++)|for (var g = 0; g < GroupCount; g++)|' src/Nordmann.LUI/Querying/Ranker.cs"

# 15. The German transliteration of ADR-0005 changes — "ä" becomes "a" instead of
#     "ae" — and the unit layer is the one that has to notice.
expect_caught "the-transliteration-is-gone" "${UNIT_PROJECT}" \
  "FullyQualifiedName~NormalizerTests" \
  "sed -i \"s|\\['ä'\\] = \\\"ae\\\";|['ä'] = \\\"a\\\";|\" src/Nordmann.LUI/Normalization/Normalizer.cs"

# 16. Two indexes share one typo dictionary (ADR-0025, owner's requirement in #48):
#     the registry is keyed by a constant, not by the name of the index — a search
#     among tags would be corrected towards names.
expect_caught "two-indexes-share-one-typo-dictionary" "${UNIT_PROJECT}" \
  "FullyQualifiedName~TypoDictionaryStateTests" \
  "sed -i 's|^            indexName,\$|            \"shared\",|' src/Nordmann.LUI/Typos/TypoDictionaries.cs"

# 16b. The bulk build of the typo dictionary forgets to commit its staging area
#      (ADR-0025): SymSpell has the words, and none of them is searchable.
expect_caught "a-build-never-commits-its-staging-area" "${UNIT_PROJECT}" \
  "FullyQualifiedName~TypoDictionaryStateTests" \
  "sed -i 's|symSpell.CommitStaged(stage);|_ = stage;|' src/Nordmann.LUI/Typos/TypoDictionary.cs"

# 17. A benchmark number in the entrance is edited by hand (ADR-0022, ADR-0039):
#     the tables of the README are rendered from benchmarks/results/, and the
#     guard notices.
expect_caught "a-benchmark-number-in-the-entrance-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBenchmarksTests" \
  "sed -i '0,/ μs |/s// ms |/' README.md"

# 18. A number in a committed BenchmarkDotNet report is edited by hand, and the
#     tables are not rendered anew (review of pull request 37, M2): the two no
#     longer agree, and the guard notices from the other side.
#     The report carries no bold — the writer of the reports drops it (ADR-0060)
#     — so the number is edited as it stands.
expect_caught "a-benchmark-report-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBenchmarksTests" \
  "sed -i '0,/ μs |/s// ms |/' benchmarks/results/Nordmann.LUI.Benchmarks.QueryBenchmarks-report-github.md"

# 19. The retired word returns to the public surface (ADR-0023, issue #41): a
#     public member is named "Owned…" again — renamed everywhere, so the tree
#     still builds and only the guard of the vocabulary can react.
expect_caught "the-old-word-returns-to-the-public-surface" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Embedded_and_Linked" \
  "grep -rl --include=*.cs CorrectionSkipped src tests | xargs sed -i 's/CorrectionSkipped/OwnedSkipped/g'"

# 20. A setting of the query exists only on one side (review of 2026-11-04, B.2):
#     LuiQueryOptions gets a property that LuiQuery has no method for.
expect_caught "an-option-without-a-method-on-the-query" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_option_of_a_query_can_be_set_on_the_query" \
  "sed -i 's|    public bool TrackTimings { get; init; }|    public bool TrackTimings { get; init; }\n\n    /// <summary>x</summary>\n    public bool Extra { get; init; }|' src/Nordmann.LUI/LuiQueryOptions.cs"

# 21. A member joins the interface that only reads (B.3). A default interface member needs no
#     implementation, so the tree builds and only the guard can react.
expect_caught "a-member-joins-the-read-interface" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_read_interface_only_reads" \
  "sed -i '\$ i\\    /// <summary>x</summary>\n    void Extra() { }' src/Nordmann.LUI/ILuiIndex.cs"

# 22. An event joins the interface that only reads (review of pull request 47, M5): an event
#     is neither a property nor a method without a special name, so the guard has to ask for events.
expect_caught "an-event-joins-the-read-interface" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_read_interface_only_reads" \
  "sed -i '\$ i\\    /// <summary>x</summary>\n    event System.EventHandler? Extra { add { } remove { } }' src/Nordmann.LUI/ILuiIndex.cs"

# 23. A write interface loses a maintenance member (B.3): the class keeps the method, so the tree builds.
expect_caught "a-write-interface-loses-a-maintenance-member" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_read_interface_only_reads" \
  "perl -0pi -e 's|    /// <summary>\\n    /// Checks the index against itself and against.*?LuiIntegrityReport Check\\(\\);\\n\\n||s' src/Nordmann.LUI/ILuiLinkedIndex.cs"

# 24. The client of the sample (PR-011, ADR-0027). Each mutation is a thing that would go wrong on a tired day, and the
#     guard `SampleClientTests` has to notice — the client has no build, so nothing else does.
SAMPLE_JS="samples/Lui.Sample/wwwroot/js"

#     a module outside net/ calls fetch: the page has one fetch boundary
expect_caught "a-module-calls-fetch-outside-the-boundary" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~SampleClientTests" \
  "echo \"export const leak = () => fetch('/api/health');\" >> ${SAMPLE_JS}/core/format.js"

#     a module writes HTML from a string
expect_caught "a-module-writes-html-from-a-string" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~SampleClientTests" \
  "echo \"export const leak = (node, text) => { node.innerHTML = text; };\" >> ${SAMPLE_JS}/core/format.js"

#     the vendored library is edited without its provenance
expect_caught "the-vendored-library-is-edited" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~SampleClientTests" \
  "echo '// edited' >> ${SAMPLE_JS}/vendor/flexdatalist.es.js"

#     a low-level module reaches up into a panel
expect_caught "core-imports-a-panel" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~SampleClientTests" \
  "sed -i \"1i import '../panels/hits-panel.js';\" ${SAMPLE_JS}/core/format.js"

#     the page gets an inline script (its policy would refuse it, but only in a browser)
expect_caught "the-page-gets-an-inline-script" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~SampleClientTests" \
  "sed -i 's|</head>|<script>window.x = 1</script></head>|' samples/Lui.Sample/wwwroot/index.html"

# 24b. The index knows no types (ADR-0035, ADR-0074): the untyped hit gets back a payload property — a type parameter's
#      view through the back door. Only the hit of a handle with a payload type carries one (`LuiHit<TKey, TPayload>`, a
#      plain value), the untyped hit and the hit of a handle without a payload type have none. It builds; only the guard of
#      the public API can react.
expect_caught "the-hit-gets-a-payload-property" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_index_knows_no_types" \
  "sed -i 's|    /// <summary>Whether the entry has a payload.</summary>|    /// <summary>x</summary>\n    public object? Payload => StoredPayload;\n\n    /// <summary>Whether the entry has a payload.</summary>|' src/Nordmann.LUI/LuiHit.cs"

# 24b2. The hit of a handle without a payload type gets a payload property (ADR-0074): a hit has a payload only where
#       the type of the payload is declared. It builds; the guard of the public API reacts.
expect_caught "the-key-hit-gets-a-payload-property" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_index_knows_no_types" \
  "sed -i 's|    /// <summary>The weight of the entry.</summary>|    /// <summary>x</summary>\n    public object? Payload => null;\n\n    /// <summary>The weight of the entry.</summary>|' src/Nordmann.LUI/LuiHitOfKey.cs"

# 24b3. The payload is not loaded with the query (ADR-0074): the hit holds the default of its type, where the payload was to
#       be. It builds; the test that reads the payload of a hit has to see the value.
expect_caught "the-payload-is-not-loaded-with-the-query" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~HitPayloadTests" \
  "sed -i 's|Payload = hit.RequirePayload<TPayload>(),|Payload = default!,|' src/Nordmann.LUI/LuiHitOfKeyAndPayload.cs"

# 24b4. A map that returns null is no failure (ADR-0074): the null check is skipped for a payload that was built.
expect_caught "a-map-that-returns-null-is-no-failure" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~HitPayloadTests" \
  "sed -i -e 's|^        stored is null\$|        stored is null \&\& serialized|' -e 's|: Read<T>(stored, key);|: Read<T>(stored!, key);|' src/Nordmann.LUI/Querying/PayloadSource.cs"

# 24b5. An entry whose payload is not stored is read as a payload (ADR-0074): the null check is skipped for stored bytes.
expect_caught "a-payload-that-is-not-stored-is-no-failure" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~CheckPayloadsTests" \
  "sed -i -e 's|^        stored is null\$|        stored is null \&\& !serialized|' -e 's|: Read<T>(stored, key);|: Read<T>(stored!, key);|' src/Nordmann.LUI/Querying/PayloadSource.cs"

# 24b6. A map that throws escapes as it threw (ADR-0074): the query does not name the index, the key and the type.
expect_caught "a-throwing-map-is-not-wrapped" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~HitPayloadTests" \
  "sed -i 's|when (_payloadType is not null && exception is not (OperationCanceledException or LuiPayloadException))|when (_payloadType is not null \&\& exception.Message.Length < 0)|' src/Nordmann.LUI/Querying/ColumnPayload.cs"

# 24b7. The runtime of a handle without a payload type selects the payload column again (ADR-0074): the bytes that the old
#       calls stored are copied for every hit of a query that has no use for them.
expect_caught "a-handle-without-a-payload-type-reads-the-payload-column" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~HitPayloadTests" \
  "sed -i 's|hitPayload: codec is null ? new NoPayload(name) : null),|hitPayload: null),|' src/Nordmann.LUI/Indexes/IndexRuntimes.cs"

# 24b8. CheckPayloads reads without the guard of the handle (ADR-0075, ADR-0074): an index that was created again with
#       another definition is read as if it were the old one.
expect_caught "check-payloads-skips-the-guard" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~CheckPayloadsTests" \
  "perl -0pi -e 's|(using var snapshot = connection.BeginTransaction\(deferred: true\);\n)        guard\?\.Check\(connection, snapshot\);\n(\n        var unreadable)|\$1\$2|' src/Nordmann.LUI/Indexes/EmbeddedCheck.cs"

# 24b9. CheckPayloads stops at the first entry it cannot read (ADR-0074): the list is not every entry.
expect_caught "check-payloads-stops-at-the-first-unreadable-entry" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~CheckPayloadsTests" \
  "sed -i 's|                unreadable.Add((key, exception.Message));|                if (unreadable.Count == 0) { unreadable.Add((key, exception.Message)); }|' src/Nordmann.LUI/Indexes/EmbeddedCheck.cs"

# 24c. A key of the other kind reaches the file (ADR-0035): the check of the kind is skipped, and a text
#      would be written into an index of numbers — a key that can never be read back.
expect_caught "a-key-of-the-other-kind-reaches-the-file" "${UNIT_PROJECT}" \
  "FullyQualifiedName~KeyCodecTests" \
  "sed -i 's|        if (key.Kind != kind)|        if (key.Kind != kind \&\& string.IsNullOrEmpty(indexName))|' src/Nordmann.LUI/Schema/KeyCodec.cs"

# 24d. The JSON payload is read tolerantly again (review of PR 60, I1): a chip read as a recipe comes back as a recipe
#      of defaults — wrong data that looks like right data — instead of an error.
expect_caught "the-json-payload-is-read-tolerantly" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadSerializerTests" \
  "sed -i '/UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,/d; /RespectRequiredConstructorParameters = true,/d' src/Nordmann.LUI/LuiPayloadSerializer.cs"

# 24e. A limit of the file of the budgets is edited by hand (issue #38, ADR-0056): the limit is the promise, and a
#      promise that drifts from the tables of the entrance and of the record is a promise nobody checked. The guard
#      reads the file and compares.
expect_caught "a-budget-limit-in-the-readme-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBudgetsTests" \
  "sed -i '0,/\"limit\": 5,/s/\"limit\": 5,/\"limit\": 6,/' tests/Nordmann.LUI.Testing/Budgets/budgets.json"

# 24f. Another limit of the file of the budgets is edited by hand (issue #38, ADR-0056): the summary of the run
#      promises what the file does not. The guard reads the file and compares.
expect_caught "a-budget-limit-in-the-record-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBudgetsTests" \
  "sed -i '0,/\"limit\": 20,/s/\"limit\": 20,/\"limit\": 21,/' tests/Nordmann.LUI.Testing/Budgets/budgets.json"

# 24g. A link of the documentation points at nothing (issue #66): the promise of the link check is that every
#      relative link of every markdown file names a file that exists. A link that points at nothing is a broken
#      promise, and the guard `LinkCheckTests` has to name it.
expect_caught "a-link-in-the-documentation-points-at-nothing" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~LinkCheckTests" \
  "echo '[a link that points at nothing](./this-file-does-not-exist.md)' >> docs/benchmarks.md"

# 25. The concurrency of the round of issue #67: the write-ahead log is switched off. Without it a reader beside a
#      writer waits for the lock and gives up with an error — the promise of ADR-0015, ADR-0046 (many readers beside one writer)
#      is gone, and the test with the second catalog has to notice.
expect_caught "a-database-without-the-write-ahead-log" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~Two_catalogs_on_one_file_see_a_consistent_picture" \
  "sed -i 's|PRAGMA journal_mode = WAL;|PRAGMA journal_mode = DELETE;|' src/Nordmann.LUI/Storage/Database.cs"

# 26. The dispose waits for the running operations, but nobody wakes it (issue #67): the catalog waits for a warm-up
#      that is still running — and the warm-up and the dispose never come back. The test has to end the wait.
expect_caught "a-dispose-that-never-wakes-the-waiter" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_dispose_during_a_warm_up_ends_and_does_not_hang" \
  "sed -i '/Monitor.PulseAll(_lifetime);/d' src/Nordmann.LUI/Storage/Database.cs"

# 26a. A round of the outbox that fails is swallowed again (ADR-0045, review of pull request 69): the warm-up catches
#      the failure and tells nobody. The test that renames the table under a running host has to miss the warning.
expect_caught "a-failed-round-of-the-outbox-is-silent" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_round_that_fails_is_told_to_the_logger_of_the_host" \
  "sed -i 's|LogRoundFailed(exception, _interval.TotalSeconds);|_ = exception;|' src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs"

# 26b. The typed hit gets a property that deserialises (ADR-0044, review of pull request 69): a property is a promise
#      of a field, and the read of a payload does work and can fail. The guard of the untyped index has to name it.
expect_caught "the-typed-hit-gets-a-payload-property" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_index_knows_no_types" \
  "sed -i 's|    public TPayload? GetPayload() => Hit.GetPayload<TPayload>();|    public TPayload? GetPayload() => Hit.GetPayload<TPayload>();\n\n    /// <summary>x</summary>\n    public TPayload? Payload => GetPayload();|' src/Nordmann.LUI/LuiTypedHit.cs"

# 26c. The provider retries a busy command for thirty seconds again (issue #70, ADR-0046): without the timeout of
#      the commands, a write waits 31 s whatever BusyTimeout says. The test of the wait has to notice.
expect_caught "the-provider-retries-for-thirty-seconds" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_write_that_waits_for_a_lock_waits_the_busy_timeout_of_the_catalog" \
  "sed -i '/connection.DefaultTimeout = ProviderRetrySeconds;/d' src/Nordmann.LUI/Storage/Database.cs"

# 26d. An action is used by its tag again (issue #74, ADR-0047): a tag can be moved by whoever owns the action, a
#      commit cannot. The guard of the workflows has to name it. The pattern names no commit and no version: the pins
#      move with every update of Dependabot, and a mutation that named one ran into nothing after the first.
expect_caught "an-action-pinned-by-its-tag" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_action_is_pinned_by_its_commit_and_names_its_version" \
  "sed -i -E '0,/actions\/checkout@[0-9a-f]{40} # v[0-9.]+/s//actions\/checkout@v4/' .github/workflows/ci.yml"

# 26e. A job loses its timeout (issue #74, ADR-0047): the default of GitHub is six hours of a hanging test.
expect_caught "a-job-without-a-timeout" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_job_has_a_timeout" \
  "sed -i '0,/    timeout-minutes: 10/{/    timeout-minutes: 10/d}' .github/workflows/ci.yml"

# 26f. The SDK of global.json becomes a channel (review of pull request 83): setup-dotnet turns "10.0.x" into the channel
#      10.0 and installs the latest 10.0 of the image — the pin is gone. The guard of the SDK has to name it. The pattern
#      names no version, for the same reason as 26d.
expect_caught "the-sdk-version-is-a-channel" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_sdk_is_the_one_of_global_json" \
  "sed -i -E 's/\"version\": \"[0-9]+\\.[0-9]+\\.[0-9]+\"/\"version\": \"10.0.x\"/' global.json"

# 26g. The release pushes before the gates are green (issue #75, ADR-0048): the push no longer needs the call of
#      ci.yml. The guard of the release has to name it.
expect_caught "the-release-skips-the-gate" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_release_runs_the_gates_of_every_pull_request_and_stores_no_key" \
  "sed -i 's/    needs: \\[tag, gate\\]/    needs: [tag]/' .github/workflows/publish.yml"

# 26h. A download of a pipeline that gives up on the first gateway error (issue #91): one 504 of github.com is a red check.
expect_caught "a-download-without-a-retry" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_download_of_a_pipeline_retries_a_transient_error" \
  "sed -i 's/curl -sSfL --retry 5 --retry-delay 10 /curl -sSfL /' .github/workflows/ci.yml"

# 26i. The same download without a retry, written as a command substitution (review of pull request 92, I1): a guard that
#      read lines looked for "curl " at the start and did not see "$(curl".
expect_caught "a-download-in-a-substitution-without-a-retry" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_download_of_a_pipeline_retries_a_transient_error" \
  "sed -i 's/          curl -sSfL --retry 5 --retry-delay 10 /          archive_body=\$(curl -sSfL /' .github/workflows/ci.yml"

# 26j. The download no longer says who answered (issue #91, review of pull request 92, M2): after -L the failing host can
#      be either one, and only the log can tell.
expect_caught "a-download-that-does-not-say-who-answered" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_download_of_a_pipeline_says_who_answered" \
  "sed -i 's/%{url_effective}/%{size_download}/' .github/workflows/ci.yml"

# 26k. A test that reads the clock without the trait (issue #78): it would run in the fast loop of "Category!=Timing",
#      and a loaded machine would turn it red with no fault.
expect_caught "a-clock-test-without-the-trait" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_test_that_reads_the_clock_carries_the_timing_trait" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\npublic sealed class ClockTests\n{\n    [Fact]\n    public void A_test()\n    {\n        _ = System.Diagnostics.Stopwatch.StartNew();\n    }\n}\n' > tests/Nordmann.LUI.Unit.Tests/ClockTests.cs"

# 26l. The trait of a class is lost (issue #78): every test of BusyTimeoutTests reads the clock.
expect_caught "a-clock-class-without-the-trait" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_test_that_reads_the_clock_carries_the_timing_trait" \
  "sed -i '/^\[Trait(\"Category\", \"Timing\")\]\$/d' tests/Nordmann.LUI.Integration.Tests/Concurrency/BusyTimeoutTests.cs"

# 26m. A polling loop without the trait (review of pull request 95, M1): the form of the test of #94 — a loop around
#      Task.Delay — was not a reading of the clock to the first version of the guard.
expect_caught "a-polling-loop-without-the-trait" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_test_that_reads_the_clock_carries_the_timing_trait" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\npublic sealed class PollTests\n{\n    [Fact]\n    public async Task A_test()\n    {\n        for (var i = 0; i < 3; i++)\n        {\n            await Task.Delay(10);\n        }\n    }\n}\n' > tests/Nordmann.LUI.Unit.Tests/PollTests.cs"

# 26n. A test that reads the clock through a helper of its file (review of pull request 95, M1): the body of the test
#      names no clock, the helper it calls does.
expect_caught "a-clock-helper-without-the-trait" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_test_that_reads_the_clock_carries_the_timing_trait" \
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\npublic sealed class HelperTests\n{\n    [Fact]\n    public void A_test()\n    {\n        _ = Measure();\n    }\n\n    private static long Measure() => System.Diagnostics.Stopwatch.GetTimestamp();\n}\n' > tests/Nordmann.LUI.Unit.Tests/HelperTests.cs"

# 26o. An action under the other spelling (issue #93): GitHub reads action.yaml as well as action.yml, and an action
#      pinned by its tag there was invisible to every rule of the pipelines.
expect_caught "an-action-yaml-pinned-by-its-tag" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_action_is_pinned_by_its_commit_and_names_its_version" \
  "mkdir -p .github/actions/probe && printf 'name: Probe\ndescription: x\nruns:\n  using: composite\n  steps:\n    - uses: actions/checkout@v4\n' > .github/actions/probe/action.yaml"

# 26p. The payload row reads an integer as an Int32 (issue #104): it compiles — an int widens to a long — and a count
#      beyond Int32 would be lost in the payload of a linked index.
expect_caught "a-payload-integer-read-as-int32" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~The_payload_row_reads_a_column_in_the_type_the_mapping_asks_for" \
  "sed -i 's|Reader.GetInt64(Ordinal(column))|Reader.GetInt32(Ordinal(column))|' src/Nordmann.LUI/LuiPayloadRow.cs"

# 26q. The linked builder drops the transliteration it was given (issue #105): a setting made after LinkTo would be
#      lost without a word, and the index would be another than the one described.
expect_caught "a-linked-setting-dropped" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~The_settings_made_after_LinkTo_reach_the_query_and_the_definition" \
  "sed -i 's|        _options = _options with { GermanTransliteration = enabled };||' src/Nordmann.LUI/LuiLinkedIndexBuilder.cs"

# 26r. The rebuild of a linked index only ensures a dictionary (issue #105): the frequencies would stay those of the
#      first build, whatever the weights of the table say now.
expect_caught "a-linked-rebuild-that-builds-nothing" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_linked_index_rebuilds_its_typo_dictionary_from_the_table_as_it_is_now" \
  "sed -i 's|        _typos.Rebuild(cancellationToken);|        _typos.EnsureBuilt(cancellationToken);|' src/Nordmann.LUI/Indexes/LinkedIndex.cs"

# 26s. The stop of the warm-up leaves the rounds running (issue #106, issue #148): the rounds would go on against a host
#      that is shutting down. The stop has two parts — the timer stops, and a round that begins after the stop does not
#      begin at all — and the mutation breaks both of them, so that the test sees a round that applies a row the
#      application wrote after the stop.
expect_caught "a-stop-that-keeps-the-rounds" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~After_the_host_stops_no_round_of_the_outbox_begins" \
  "sed -i -e 's|        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);||' -e 's|        _stopping = true;|        _stopping = false;|' src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs"

# 26t. The guard against overlapping rounds lets every tick in (issue #106): while the write lock is held, a round per
#      tick would wait beside the others.
expect_caught "rounds-of-the-outbox-that-overlap" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_round_that_waits_for_the_write_lock_is_not_joined_by_the_next_one" \
  "sed -i 's|        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)|        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0 \&\& false)|' src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs"

# 26u. Two empty texts reach the division of the measure (issue #107): 0 / 0 is not a number, and a ranking that sorts by
#      it has no order.
expect_caught "two-empty-texts-divided-by-zero" "${UNIT_PROJECT}" \
  "FullyQualifiedName~SimilarityTests" \
  "sed -i '0,/        if (first.SequenceEqual(second))/s//        if (first.SequenceEqual(second) \&\& first.Length > 0)/' src/Nordmann.LUI/Similarity/DamerauMeasure.cs"

# 26u'. The same for the other measure whose branch was removed (review of pull request 113, M1): the rule is one, the
#       files are two.
expect_caught "two-empty-texts-divided-by-zero-levenshtein" "${UNIT_PROJECT}" \
  "FullyQualifiedName~SimilarityTests" \
  "sed -i '0,/        if (first.SequenceEqual(second))/s//        if (first.SequenceEqual(second) \&\& first.Length > 0)/' src/Nordmann.LUI/Similarity/LevenshteinMeasure.cs"

# 26v. A batch written during a build of the typo dictionary is not queued (issue #107): the words of a Sync during a
#      rebuild would be lost with the swap.
expect_caught "a-batch-lost-with-the-swap" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_batch_written_during_a_rebuild_is_not_lost_with_the_swap" \
  "sed -i 's|                    pending.Enqueue(text);|                    _ = text;|' src/Nordmann.LUI/Typos/TypoDictionary.cs"

# 26w. Get<T> asks no type whether it can hold null (issue #111, ADR-0052): a NULL read as a long? would throw again, as
#      it did before 0.1.0.
expect_caught "a-null-read-as-null-for-every-type-refused" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_null_column_is_null_for_a_type_that_can_hold_null_and_refused_for_one_that_cannot" \
  "sed -i 's|        return default(T) is null \&\& reader.IsDBNull(ordinal)|        return false \&\& reader.IsDBNull(ordinal)|' src/Nordmann.LUI/LuiPayloadRow.cs"

# 26x. The distance of the index is not the distance of its definition (ADR-0054): an opened index forgets how its
#       dictionary was built, and a query has to ask for the correction again. The test that opens the catalog a
#       second time has to notice.
expect_caught "the-distance-does-not-survive-a-restart" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~An_opened_index_keeps_its_distance_and_corrects_without_the_query_asking_for_it" \
  "sed -i 's|typoDistance: entry.Definition.TypoDistance);|typoDistance: LuiTypoDistance.None);|' src/Nordmann.LUI/LuiCatalog.cs"

# 26y. The distance of the index never reaches its dictionary (ADR-0054): every index forgives one error, whatever
#       its definition says. The index that forgives nothing would build a dictionary — and pay for it.
expect_caught "the-distance-of-the-index-never-reaches-its-dictionary" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~An_index_that_forgives_nothing_builds_no_dictionary" \
  "sed -i 's|Distance: distance));|Distance: LuiTypoDistance.OneError));|' src/Nordmann.LUI/Typos/TypoDictionaries.cs"

# 26z. The correction always forgives one error (ADR-0054): the enumeration is a number again, and the index that
#       forgives two errors forgives one. The typo of two errors has to stay uncorrected.
expect_caught "the-correction-always-forgives-one-error" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_typo_of_two_errors_is_corrected_by_the_index_that_forgives_two_and_by_no_other" \
  "sed -i 's|suggestions.Add(Suggest(symSpell, search.AsSpan(word.Start, word.Length), (int)_distance));|suggestions.Add(Suggest(symSpell, search.AsSpan(word.Start, word.Length), 1));|' src/Nordmann.LUI/Typos/TypoDictionary.cs"

# 26aa. The guard of the distance None sits in the caller that asks, not in the thing that builds
#       (ADR-0054, review of pull request #126, I1): the guard fires only when the dictionary is already
#       built — and it never is, while a build starts. A rebuild of an index that forgives nothing builds
#       the dictionary: 138 MB for a correction that is never made.
expect_caught "a-rebuild-that-forgives-nothing-builds-a-dictionary" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~A_rebuild_of_an_index_that_forgives_nothing_builds_no_dictionary" \
  "sed -i 's|        if (_distance == LuiTypoDistance.None)|        if (IsBuilt \&\& _distance == LuiTypoDistance.None)|' src/Nordmann.LUI/Typos/TypoDictionary.cs"
# 26y. The query path drops a duplicate again (ADR-0055): the uniqueness of a text belongs to the data, so the
#      library answers with one hit per entry — a candidate whose search form was seen is dropped again, and the
#      four restated tests notice.
expect_caught "a-duplicate-of-a-text-is-dropped-again" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~Two_spellings_of_one_text_are_two_hits|FullyQualifiedName~The_priority_leads_the_hits|FullyQualifiedName~The_correction_is_not_swallowed|FullyQualifiedName~The_input_and_its_correction_keep" \
  "sed -i 's|        var read = 0;|        var seen = new HashSet<string>(StringComparer.Ordinal);\\n        var read = 0;|' src/Nordmann.LUI/Querying/CandidateReader.cs && sed -i 's|            candidates\\[count++\\] = new Scored { RowId = reader.GetInt64(0), Text = text, WordStart = start, Variant = variant };|            if (seen.Add(Normalizer.ToSearchForm(text, _normalization))) { candidates[count++] = new Scored { RowId = reader.GetInt64(0), Text = text, WordStart = start, Variant = variant }; }|' src/Nordmann.LUI/Querying/CandidateReader.cs"

# 26ab. The stop of the warm-up does not wait for the round that runs (issue #148): the round holds the file of the
#      application, and the host that stopped the service is gone while LUI still writes (ADR-0043). The test of the
#      stop has to notice.
expect_caught "a-stop-that-does-not-wait-for-the-round" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~The_host_waits_for_a_round_that_runs_when_it_is_disposed" \
  "sed -i '/^        JoinRound();$/d' src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs"

# 27. A record that names a file that is not there (issue #137): the files of the measurements moved
#       twice, and a path that points at nothing is a broken promise.
expect_caught "a-record-that-names-a-file-that-is-not-there" "${ARCHITECTURE_PROJECT}" \
  "Every_path_of_a_record_names_a_file_that_exists" \
  "sed -i 's|tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs|tests/Nordmann.LUI.Performance.Tests/BudgetPerformanceGone.cs|' .agents/adr/0056-the-budgets-live-in-a-file.md"

# 28. A record that is not in the index of the records (issue #129): the index is written by hand, and a
#       record that is missing from it is invisible until a reader looks for it.
expect_caught "a-record-that-is-not-in-the-index" "${ARCHITECTURE_PROJECT}" \
  "Every_record_of_the_decisions_has_a_row_in_the_index" \
  "sed -i '/| \[0063\](\.\/0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied\.md)/d' .agents/adr/README.md"

# 28b. A row of the index that points at a record that is not there (issue #129): a broken pointer in the
#       place a reader looks for a decision.
expect_caught "a-row-that-points-at-a-record-that-is-not-there" "${ARCHITECTURE_PROJECT}" \
  "Every_row_of_the_index_points_at_a_record_that_exists" \
  "sed -i 's|(\./0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied\.md)|(./0063-the-surface-of-0-1-0-is-gone.md)|' .agents/adr/README.md"

# 29. A page that names a member the type does not have (issue #130): the page named `TypoTolerance` on a type
#       that has `TypoDistance`, and the guard of the types accepted it — it compared types, not members.
expect_caught "a-page-that-names-a-member-the-type-does-not-have" "${ARCHITECTURE_PROJECT}" \
  "The_page_names_no_member_that_the_type_does_not_have" \
  "sed -i 's|LuiTypoDistance TypoDistance { get; }|LuiTypoDistance TypoTolerance { get; }|' docs/api.md"

# 30. A block of the tutorial that does not compile (issue #128): the guard compiles the blocks of the
#      documentation and compares what they print with the pages — a block that names an API the
#      library does not have has to fail the lane.
expect_command_caught "a-tutorial-block-that-does-not-compile" \
  "${PYTHON} tools/tutorial-check.py" \
  "sed -i 's|index.SingleWord()|index.SingleWordTypo()|' docs/tutorial/01-first-index.md" \
  "docs/tutorial/01-first-index.md"

# 31. A page of the tutorial that says something else than the code does (issue #128): the text block
#      of the page is the output of the block above it, and the guard compares the two.
expect_command_caught "a-tutorial-page-that-says-something-else" \
  "${PYTHON} tools/tutorial-check.py" \
  "sed -i 's|^1: vegan (120)\$|1: vegan (999)|' docs/tutorial/01-first-index.md" \
  "docs/tutorial/01-first-index.md"

# 32. A probe that does not name the iteration that allocated (issue #134): the report of a dirty
#      window has to name the iteration and the amount. A report that says only "something
#      allocated" sends the next one after the wrong thing — and the run of 43228720565 said
#      only that 1912 bytes were allocated.
expect_caught "a-probe-that-does-not-name-the-iteration" "${UNIT_PROJECT}" \
  "The_probe_names_the_iteration_that_allocated" \
  "sed -i 's|  iteration {i}: {delta} bytes|  {i}: {delta}|' tests/Nordmann.LUI.Unit.Tests/Support/AllocationProbe.cs"

# 33. A count threshold that drops the light words (issue #44): the contract with SymSpell says that a word with the
#      weight zero is a word — SymSpell drops a count of 0, so LUI adds one. A threshold of two would drop the light
#      words, and the correction of them would be gone.
expect_caught "a-count-threshold-that-drops-the-light-words" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~TypoContractTests.The_count_threshold_is_one" \
  "sed -i 's|countThreshold: 1|countThreshold: 2|' src/Nordmann.LUI/Typos/TypoDictionary.cs"

# 34. A run that does not name the failing test (issue #154): the failure of that day was lost, because the
#      output of the run was filtered, and what was left of it was the count. A procedure that keeps the whole
#      output has to name the test that failed — the name is the answer the issue #154 is still missing.
expect_command_caught "a-run-that-does-not-name-the-failing-test" \
  "bash tools/load-run.sh --self-test" \
  "sed -i 's|printf(\"the failing test: %s|printf(\"a test failed:|' tools/load-run.sh" \
  "Nordmann.LUI.Integration.Tests.Catalog.A_test_that_failed"

# 35. A claim of a round that does not stay true (issue #185): the claim of a round that has begun is momentary — the
#      round of the test waits for the write lock of the file, which the test itself holds, so it lives about a second,
#      and a loaded machine can delay the poll of a test by more than that. A waiter that comes late must not miss the
#      round, so the claim has to stay true.
expect_caught "a-claim-of-a-round-that-does-not-stay-true" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~The_claim_of_a_round_that_has_begun_stays_true_when_the_round_is_over" \
  "sed -i 's|            Volatile.Write(ref _running, 0);|            Interlocked.Exchange(ref _roundsBegun, 0);\n            Volatile.Write(ref _running, 0);|' src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs"

# 35a. A handle that never looks at the SQLite schema counter (issue #265, ADR-0075): the index that another process dropped
#       and created again with another definition is read and written through the stale definition. The test with the two
#       catalogs has to see the old handle go on.
expect_caught "a-handle-that-never-looks-at-the-schema-counter" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~TwoCatalogsTests.After_a_drop_and_a_recreate_by_one_catalog_the_old_handle_of_the_other_throws" \
  "sed -i 's#        if (counter == Volatile.Read(ref _validatedAt)#        if (true || counter == Volatile.Read(ref _validatedAt)#' src/Nordmann.LUI/Indexes/IndexGuard.cs"

# 35b. An open call that forgets the handle it handed out (issue #265, ADR-0075): every call builds a handle, and the thin
#       handle — the same instance for the same definition, nothing allocated — is gone.
expect_caught "an-open-call-that-forgets-its-handle" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~ThinHandleTests.Index_returns_the_same_instance_for_the_same_definition" \
  "sed -i 's|            _handles.AddOrUpdate(definition, new HandleOfDefinition(handle, runtime.Guard));|            _ = new HandleOfDefinition(handle, runtime.Guard);|' src/Nordmann.LUI/LuiCatalog.cs"

# 35c. A name that is not remembered as missing (issue #265, ADR-0075): the unknown name reloads the definitions at every
#       call, and a request for an index that is not there is a read of the whole of lui_meta.
expect_caught "a-missing-name-that-is-not-remembered" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~ThinHandleTests.An_unknown_name_reloads_once_then_throws_and_does_not_reload_again_while_the_counter_is_unchanged" \
  "sed -i 's|            _ = snapshot.Missing.TryAdd(name, 0);|            _ = snapshot.Missing.Count;|' src/Nordmann.LUI/Indexes/StoredIndexes.cs"

# 35d. A drop that leaves the runtime of the index alive (issue #265, ADR-0075): the handle of the dropped index is not
#       stale, and the next index of that name gets the typo dictionary and the handle of the old one.
expect_caught "a-drop-that-leaves-the-runtime-alive" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~ThinHandleTests.A_handle_of_an_index_that_this_catalog_dropped_stays_dropped_and_the_open_call_gives_a_new_one" \
  "sed -i 's|        StaleHandlesOf(name);|        _ = name;|' src/Nordmann.LUI/LuiCatalog.cs"

# 35e. A query that does not ask the guard (issue #265, ADR-0075): the search of a handle reads through the definition it was
#       built from, whatever the file says now.
expect_caught "a-query-that-does-not-ask-the-guard" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~TwoCatalogsTests.After_a_drop_and_a_recreate_by_one_catalog_the_old_handle_of_the_other_throws" \
  "sed -i 's|        _guard?.Check(connection, snapshot);|        _ = _guard;|' src/Nordmann.LUI/Querying/QueryRunner.cs"

# 35f. A write whose transaction does not hold the write lock (issue #265, review of pull request 286): the check of the schema
#       counter and the statement are two steps again, and another process can drop the index and create it again in between —
#       the write lands in the new index, or fails with a bare SQLite error. The test that swaps the index between the two has
#       to see it.
expect_caught "a-write-that-does-not-hold-the-lock-between-check-and-statement" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~AtomicCheckTests.Upsert_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write" \
  "sed -i 's|^            var transaction = connection.BeginTransaction(deferred: false);|            var transaction = connection.BeginTransaction(deferred: true);|' src/Nordmann.LUI/Indexes/EmbeddedIndex.cs"

# 35g. A count that checks outside its snapshot (issue #265, review of pull request 286): the check and the read are two
#       snapshots, and the count can be the one of the index that was created again.
expect_caught "a-count-that-checks-outside-its-snapshot" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~AtomicCheckTests.Count_answers_for_the_index_it_checked_or_throws_and_never_reads_the_new_one" \
  "sed -i 's|        _guard?.Check(connection, snapshot);|        _guard?.Check(connection);|' src/Nordmann.LUI/Indexes/EmbeddedIndex.cs"

# 35h. A decision that is made on a snapshot another thread has replaced (issue #265, review of pull request 286): the open
#       call throws a mismatch from the stale entry — and the message advises a drop. The test in which another thread
#       reloads first has to see the false exception.
expect_caught "an-open-call-that-throws-from-a-replaced-snapshot" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~OpenCallTests.A_mismatch_that_was_judged_on_a_snapshot_another_thread_has_replaced_is_decided_again" \
  "sed -i 's#        return ReloadIfChanged() || Current.Counter != judged.Counter;#        return ReloadIfChanged();#' src/Nordmann.LUI/Indexes/StoredIndexes.cs"

# 35i. A guard that judges the newest definitions and not the snapshot of its operation (issue #265, review of pull request 286):
#       a read that pinned its snapshot between a drop and a create runs on tables that are not there.
expect_caught "a-guard-that-judges-another-snapshot-than-the-operation" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~GuardTests.A_read_that_pinned_its_snapshot_between_a_drop_and_a_create_ends_in_the_proper_exception" \
  "sed -i 's|IndexStore.ReadIndex(connection, transaction, Name);|IndexStore.ReadIndex(_stored.Database, Name);|' src/Nordmann.LUI/Indexes/IndexGuard.cs"

# 35j. A dictionary that is not looked at after it was built (issue #265, review of pull request 286): the texts of an index that
#       was created again under the handle are the dictionary of the old one.
expect_caught "a-dictionary-that-is-not-checked-after-its-build" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~AtomicCheckTests.The_typo_dictionary_of_a_handle_does_not_take_in_the_texts_of_an_index_that_was_created_again_under_it" \
  "sed -i 's|            CheckAfterTheWork();|            _ = _guard;|' src/Nordmann.LUI/Indexes/EmbeddedIndex.cs"

# 35k. The distance of the stored definition does not reach the embedded index that the typed way opens (issue #265, ADR-0054, review of
#       pull request 286): the old mutation 26x bites on the old path only.
expect_caught "the-typed-path-forgets-the-distance-of-the-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~An_index_opened_through_its_definition_keeps_its_distance_and_corrects_without_the_query_asking_for_it" \
  "sed -i 's|^                    typoDistance: entry.Definition.TypoDistance,|                    typoDistance: LuiTypoDistance.None,|' src/Nordmann.LUI/Indexes/IndexRuntimes.cs"

# 35l. A call that builds its handle on a snapshot that DropIndex has replaced (issue #265, ADR-0075, review of pull request 286):
#       the handle escapes the drop and works on the index that is created again.
expect_caught "a-handle-built-on-a-snapshot-that-a-drop-replaced" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~OpenCallTests.A_handle_that_was_judged_before_a_drop_and_made_after_it_is_not_returned_on_the_old_snapshot" \
  "sed -i 's|                if (StoredIndexes.Current.Counter != snapshot.Counter)|                if (snapshot is null)|' src/Nordmann.LUI/LuiCatalog.cs"

# 35m. A DropIndex that does not hold the lock of the handles (issue #265, ADR-0075, review of pull request 286): a call that has
#       judged the stored definition comes to the lock while the handles are marked, and builds an unmarked handle that works on
#       the index that is created again.
expect_caught "a-drop-without-the-lock-of-the-handles" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~OpenCallTests.A_call_that_waits_for_the_lock_while_DropIndex_marks_and_reads_the_definitions_does_not_escape_it" \
  "perl -0pi -e 's|lock \(_handleGate\)\n        \{\n            // the words of the dropped|if (name is not null)\n        {\n            // the words of the dropped|' src/Nordmann.LUI/LuiCatalog.cs"

# 35n. A DropIndex that reads the definitions again after it let go of the lock (issue #265, ADR-0075, review of pull request 286):
#       a call that waits for the lock sees the old snapshot and builds on it.
expect_caught "a-drop-that-reads-the-definitions-after-the-lock" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~OpenCallTests.A_call_that_waits_for_the_lock_while_DropIndex_marks_and_reads_the_definitions_does_not_escape_it" \
  "perl -0pi -e 's|            _ = StoredIndexes.Reload\(\);\n        \}\n\n        return existed;|        }\n\n        _ = StoredIndexes.Reload();\n        return existed;|' src/Nordmann.LUI/LuiCatalog.cs"

# 35o. The distance of the stored definition does not reach the linked index that the typed way opens (the twin of 35k).
expect_caught "the-typed-path-forgets-the-distance-of-the-linked-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~TypedLinkedPayloadTests.WithPayload_and_options_in_any_order" \
  "sed -i 's|^                typoDistance: entry.Definition.TypoDistance,|                typoDistance: LuiTypoDistance.None,|' src/Nordmann.LUI/Indexes/IndexRuntimes.cs"

# 35p. An entry that cannot be read is thrown without a look at the file (issue #265, review of pull request 286): the host that
#       opened a file with an index of an older fold keeps throwing the mismatch after a migrator dropped and created the index.
expect_caught "an-unreadable-row-thrown-without-a-look-at-the-file" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~OpenCallTests.A_stale_snapshot_whose_row_is_of_another_fold_is_looked_at_against_the_file_and_does_not_fail_for_ever" \
  "sed -i 's|            if (!StoredIndexes.Refresh(snapshot))|            if (unreadable is not null \|\| !StoredIndexes.Refresh(snapshot))|' src/Nordmann.LUI/LuiCatalog.cs"

# 35q. The registry gets a catch-all (issue #260, ADR-0072): a method that takes the serializer of the old calls registers one serializer for
#       every type — the owner's rule is that there is none. It builds; only the guard of the shape can react.
expect_caught "the-registry-gets-a-catch-all" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_payload_registry_has_no_catch_all_and_binds_the_serializer_to_the_type" \
  "sed -i 's|    /// <summary>The copy the catalog works on: what is registered now, and nothing that is registered later.</summary>|    /// <summary>x</summary>\n    public LuiPayloadRegistry Use(ILuiPayloadSerializer serializer) => this;\n\n    /// <summary>The copy the catalog works on: what is registered now, and nothing that is registered later.</summary>|' src/Nordmann.LUI/LuiPayloadRegistry.cs"

# 35r. Two contexts that know a type: the first one wins (issue #260, ADR-0072): the order of the Use calls decides — the silent precedence the
#       registry refuses.
expect_caught "two-contexts-that-know-a-type-the-first-wins" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadRegistryTests.Two_contexts_that_know_a_type_throw_naming_both" \
  "sed -i 's|        switch (known.Count)|        switch (Math.Min(known.Count, 1))|' src/Nordmann.LUI/Querying/FrozenPayloadRegistry.cs"

# 35s. An explicit registration loses to a context (issue #260, ADR-0072): the lookup skips the registrations of Use<T>.
expect_caught "an-explicit-registration-loses-to-a-context" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadRegistryTests.Use_of_T_wins_over_a_context" \
  "sed -i 's|if (_serializers.TryGetValue(type, out var registered))|if (_serializers.TryGetValue(typeof(void), out var registered))|' src/Nordmann.LUI/Querying/FrozenPayloadRegistry.cs"

# 35t. The registry is not frozen (issue #260, ADR-0072): the catalog works on the dictionary the application can still change.
expect_caught "the-registry-is-not-frozen" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadRegistryTests.A_change_after_the_catalog_was_opened_has_no_effect" \
  "sed -i 's|new FrozenPayloadRegistry(new Dictionary<Type, PayloadCodec>(_serializers), _contexts)|new FrozenPayloadRegistry(_serializers, _contexts)|' src/Nordmann.LUI/LuiPayloadRegistry.cs"

# 35u. The format is not part of the stored definition (issue #260, ADR-0072): the same type in another format is read silently.
expect_caught "the-format-is-not-part-of-the-fingerprint" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~PayloadFingerprintTests.The_same_type_in_another_format_throws_a_mismatch_naming_both_formats" \
  "sed -i 's|PayloadFormat: payload?.Format)|PayloadFormat: null)|' src/Nordmann.LUI/Schema/IndexDescription.cs"

# 35v. The registry and the serializer of the old calls are both accepted (issue #260, ADR-0072): a catalog has one way to serialize.
expect_caught "the-registry-and-the-old-serializer-are-both-accepted" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~PayloadFingerprintTests.Setting_Payloads_and_PayloadSerializer_throws" \
  "sed -i 's|if (options.Payloads is not null \&\& options.PayloadSerializer is not null)|if (options.Payloads is not null \&\& options.PayloadSerializer is null)|' src/Nordmann.LUI/LuiCatalog.cs"

# 35w. A type the registry does not know is made a payload error on the read path (issue #260, ADR-0072): a configuration error is not a payload error.
expect_caught "a-configuration-error-is-wrapped-as-a-payload-error" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~PayloadFingerprintTests.The_old_read_path_asks_the_registry_for_the_type_and_a_missing_one_is_a_configuration_error_not_a_payload_error" \
  "sed -i 's|when (exception is not (OperationCanceledException or LuiConfigurationException))|when (exception is not OperationCanceledException)|' src/Nordmann.LUI/Querying/PayloadSource.cs"

# 35x. The name of a payload type carries the assembly version (issue #260, ADR-0072): a rebuild with another version is a changed definition.
expect_caught "the-name-of-a-payload-type-carries-the-assembly-version" "${UNIT_PROJECT}" \
  "FullyQualifiedName~PayloadTypeNameTests" \
  "sed -i 's|_ = builder.Append(arity < 0 ? name : name\[..arity\]);|_ = builder.Append(arity < 0 ? name : name[..arity]).Append(type.Assembly.GetName().Version);|' src/Nordmann.LUI/Schema/PayloadTypeName.cs"

# 35y. Object is a payload type (issue #260, review of pull request 288): a source-generated context answers for System.Object, and an index of
#       object holds the payloads of any type — the catch-all that the registry refuses.
expect_caught "object-is-a-payload-type" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadRegistryTests.A_payload_type_that_is_no_single_type_is_refused_also_when_a_context_answers_for_it" \
  "sed -i 's|type == typeof(object) ? \"object\"|type == typeof(void) ? \"object\"|' src/Nordmann.LUI/Schema/PayloadTypeName.cs"

# 35z. The old write path by name ignores the fingerprint (issue #260, ADR-0072, review of pull request 288): the serializer of the old
#       calls writes bytes of its own format into an index that stores another one.
expect_caught "the-old-write-path-ignores-the-fingerprint" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~PayloadFingerprintTests.The_old_write_path_by_name_refuses_a_payload_for_an_index_that_stores_a_format" \
  "sed -i 's|PayloadFormat: { } storedFormat }$|PayloadFormat: { } storedFormat } \&\& storedFormat.Length < 0|' src/Nordmann.LUI/LuiCatalog.cs"

# 35za. A context that fails is cached as the exception it threw (issue #260, review of pull request 288): the same instance on every use, without
#       the name of the definition.
expect_caught "a-failing-context-is-cached-as-its-own-exception" "${UNIT_PROJECT}" \
  "FullyQualifiedName~LuiPayloadRegistryTests.A_context_that_fails_is_kept_as_a_failure_and_thrown_anew_naming_the_definition" \
  "sed -i 's|catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))|catch (LuiConfigurationException exception)|' src/Nordmann.LUI/Querying/FrozenPayloadRegistry.cs"

# 35zb. The first sync clears the flag before it has run (issue #272, ADR-0077; the sync of #271 in LinkedSync): a sync that is cancelled after some batches would leave the
#       index reporting that it is synced — an index that is half filled, which the application believes it need not fill.
expect_caught "a-cancelled-sync-clears-the-initial-sync-flag" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_cancelled_sync_keeps_its_batches_leaves_NeedsInitialSync_true_and_releases_the_lease" \
  "sed -i 's|            _taken = true;|            _taken = true;\n            IndexStore.MarkSynced(connection, transaction, _name);|' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zc. A complete sync does not clear the flag (issue #272, ADR-0077): the application would sync at every start.
expect_caught "a-complete-sync-leaves-the-initial-sync-flag" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedStateTests.It_becomes_false_after_a_complete_sync" \
  "sed -i 's|        IndexStore.MarkSynced(connection, transaction, _name);||' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zd. The state is read without the check of the guard (issue #272, ADR-0075, ADR-0077): an index that was created again with
#       another definition would answer as if nothing had happened.
expect_caught "the-initial-sync-flag-is-read-without-the-guard" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedStateTests.The_state_of_an_index_that_was_created_again_with_another_definition_is_a_mismatch" \
  "perl -0pi -e 's|        _guard\\?\\.Check\\(connection, snapshot\\);\\n        return IndexStore.ReadNeedsInitialSync|        return IndexStore.ReadNeedsInitialSync|' src/Nordmann.LUI/Indexes/LinkedIndex.cs"

# 35ze. CreateTriggers drops and creates the triggers that are current (issue #272, ADR-0077): the triggers an application installed
#       itself would be rewritten by a call that has nothing to do.
expect_caught "create-triggers-on-current-drops-and-recreates" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedTriggerTests.CreateTriggers_does_nothing_on_Current" \
  "sed -i -e 's|            if (state == LuiTriggerState.Current)|            if (state == (LuiTriggerState)(-1))|' -e 's|            if (state == LuiTriggerState.Stale)|            if (state != LuiTriggerState.None)|' src/Nordmann.LUI/Indexes/LinkedIndex.cs"

# 35zf. Create installs the triggers of a linked index (issue #272, ADR-0077): LUI would change the application's table on its own
#       (ADR-0015).
expect_caught "create-installs-the-triggers-of-a-linked-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedTriggerTests.Create_of_a_linked_index_installs_no_triggers" \
  "perl -0pi -e 's|(                WriteDefinition\\(connection, transaction, name, definition, createdAt\\);\\n)|\$1                if (source is LinkedSource linked)\\n                {\\n                    Execute(connection, transaction, linked.TriggerScript());\\n                }\\n|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zg. The open does not look for the column of the state (issue #272, ADR-0077, review of pull request 290): a file of an early build
#       would fail later, at the first read or write of the state, with a SQLite error.
expect_caught "an-early-build-file-without-the-state-column-is-opened" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~FormatVersionTests.A_format_2_file_of_an_early_build_without_the_state_column_is_rejected_on_open" \
  "sed -i 's#if (missing.Count > 0 \&\& HasAnyIndex(connection))#if (missing.Count > 0 \&\& !HasAnyIndex(connection))#' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zh. The open refuses a lui_meta of 0.3.0 that has no row (issue #272, review of pull request 290, round 2): a catalog without an
#       index has no format to reject, and its first Create adds the column of the state.
expect_caught "an-empty-catalog-of-0-3-0-is-refused-as-an-early-build" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~FormatVersionTests.A_catalog_of_0_3_0_without_an_index_still_opens_and_its_first_create_adds_the_state_column" \
  "sed -i 's#if (missing.Count > 0 \\&\\& HasAnyIndex(connection))#if (missing.Count > 0)#' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zi. The first Create does not add the column to an empty lui_meta (issue #272, ADR-0077): a linked index could not write its flag.
expect_caught "the-first-create-does-not-add-the-state-column" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~FormatVersionTests.A_catalog_of_0_3_0_without_an_index_still_opens_and_its_first_create_adds_the_state_column" \
  "sed -i 's#        EnsureStateColumns(database, connection, transaction);##' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zj. The sync does not look at its lease (issue #271, ADR-0077): a sync that was stopped for longer than the lease and whose lease another sync took over would
#       write its next batch beside the new owner — two syncs of one index, and the removal of the first would delete what the second wrote.
expect_caught "a-batch-does-not-check-the-lease" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_lease_that_was_taken_over_stops_the_old_owner_and_the_old_owner_leaves_the_lease_of_the_new_one_alone" \
  "sed -i '/Renew(writer, transaction, throwWhenLost: true);/d' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zk. A sync takes a lease that another sync still holds (issue #271, ADR-0077): two syncs of one index at once.
expect_caught "a-sync-takes-a-lease-that-is-still-valid" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_second_concurrent_sync_throws_sync_in_progress_naming_the_owner_and_the_expiry" \
  "sed -i 's/ AND (sync_lease_owner IS NULL OR sync_lease_expires IS NULL OR sync_lease_expires <= \$now)//' src/Nordmann.LUI/Indexes/SyncLease.cs"

# 35zl. A sync releases a lease that is not its own (issue #271, ADR-0077): the old owner that was taken over would free the lease of the new one in its finally.
expect_caught "a-release-frees-a-lease-of-another-owner" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_lease_that_was_taken_over_stops_the_old_owner_and_the_old_owner_leaves_the_lease_of_the_new_one_alone" \
  "sed -i 's/ WHERE index_name = \$name AND sync_lease_owner = \$owner;\";/ WHERE index_name = \$name;\";/' src/Nordmann.LUI/Indexes/SyncLease.cs"

# 35zm. The removal of the old generation runs before the batches (issue #271, ADR-0077): a sync that is cancelled would have deleted the previous state
#       before the new one is complete.
expect_caught "the-old-generation-is-removed-before-the-batches-are-done" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_cancelled_sync_deletes_no_row_of_the_previous_generation" \
  "perl -0pi -e 's|(                WriteBatches\\(writer, options, cancellationToken\\);\\n)(                RemoveWhatIsOld\\(writer, options, cancellationToken\\);\\n)|\$2\$1|' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zn. The removal takes the current generation with the old ones (issue #271, ADR-0077): everything the batches wrote is removed again.
expect_caught "the-removal-takes-the-current-generation-too" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.After_the_sync_the_index_holds_exactly_the_rows_of_the_table" \
  "sed -i 's/WHERE gen > -\$gen AND gen < \$gen ORDER BY/WHERE gen > -\$gen AND gen <= \$gen ORDER BY/' src/Nordmann.LUI/Schema/LinkedSource.cs"

# 35zo. A batch does not set FTS5's automerge back (issue #271, ADR-0077, ADR-0016): the index would not merge on its own after the sync.
expect_caught "a-batch-leaves-the-automerge-off" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_automatic_merge_of_FTS5_is_back_on_after_every_batch_and_every_step_of_the_removal" \
  "sed -i '0,/        Execute(connection, transaction, _source.AutomergeSql(DefaultAutomerge));/{//d}' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zp. A step of the removal does not set automerge back (issue #271, ADR-0077).
expect_caught "a-removal-step-leaves-the-automerge-off" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_automatic_merge_of_FTS5_is_back_on_after_every_batch_and_every_step_of_the_removal" \
  "perl -0pi -e 's|        Execute\\(connection, transaction, _source.AutomergeSql\\(DefaultAutomerge\\)\\);\\n        return stale.Count;|        return stale.Count;|' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zq. A batch is not guarded (issue #271, ADR-0075, ADR-0077): the index that another process created again between two batches would be written with the old definition's statements.
expect_caught "a-batch-of-the-sync-is-not-guarded" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.An_index_created_again_with_another_definition_between_two_batches_is_a_definition_mismatch" \
  "sed -i '/            _guard?.Check(writer, transaction);/d' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zr. The merge ends after its first step (issue #271, ADR-0077): an index that was written in a hundred segments stays in a hundred segments, and the flag is cleared by a step that was not the last.
expect_caught "the-merge-stops-after-one-step" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_merge_is_done_when_the_sync_returns_and_the_flag_is_cleared_by_its_last_step" \
  "sed -i 's/if (Total(connection, transaction) - before > 1)/if (Total(connection, transaction) - before > 1_000_000_000)/' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zs. A single refresh writes generation 0 (issue #271, ADR-0077): the row that a refresh wrote beside a sync is removed with the old generation at its end.
expect_caught "a-refresh-writes-the-wrong-generation" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_row_that_a_refresh_wrote_beside_the_sync_stays_when_the_old_generation_is_removed" \
  "sed -i 's|substr({prefix}, 1, 2), {CurrentGeneration} \"|substr({prefix}, 1, 2), 0 \"|' src/Nordmann.LUI/Schema/LinkedSource.cs"

# 35zt. The lease reads the system clock and not the one of the catalog (issue #271, ADR-0077): a test — and an application that fakes its time — cannot say when a lease expires.
expect_caught "the-lease-ignores-the-clock-of-the-catalog" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_lease_is_a_row_of_lui_meta_in_utc_and_it_is_renewed_with_every_batch" \
  "sed -i 's/private long Now() => _database.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();/private long Now() => _database.TimeProvider is null ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();/' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zu. The keys a batch brought in step stay in the outbox (issue #271, ADR-0077): ApplyPending would do the work of the sync again.
expect_caught "a-batch-leaves-its-keys-in-the-outbox" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedTriggerTests.A_refresh_and_a_sync_take_their_rows_out_of_the_outbox" \
  "sed -i 's/, _source.BatchForgetPendingSql//' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zv. The generation column has no index (issue #271, ADR-0077): every step of the removal would read the whole table of the index.
expect_caught "the-generation-column-has-no-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_table_of_LUI_has_a_generation_column_that_cannot_be_null_and_an_index_on_it" \
  "sed -i '/CREATE INDEX {EntryTable}_gen ON {EntryTable}(gen);/d' src/Nordmann.LUI/Schema/LinkedSource.cs"

# 35zw. The open check forgets a column of the sync (issue #271, ADR-0077): a file of the build of #272 would fail later, with a SQLite error, at the first sync.
expect_caught "an-early-file-without-the-lease-column-is-opened" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~FormatVersionTests.A_format_2_file_of_an_early_build_without_a_column_of_the_sync_is_rejected_on_open" \
  "sed -i '/(\"sync_lease_expires\", \"INTEGER\"),/d' src/Nordmann.LUI/Schema/Meta.cs"

# 35zx. The linked sync clears the table before it fills it (issue #271, ADR-0077): a search during the sync would see an empty index.
expect_caught "the-linked-sync-clears-the-index-first" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_search_during_the_sync_never_sees_an_empty_index" \
  "perl -0pi -e 's|            _taken = true;\\n|            _taken = true;\\n            Execute(connection, transaction, \\x22DELETE FROM lui_\\x22 + _name + \\x22; INSERT INTO lui_\\x22 + _name + \\x22_fts(lui_\\x22 + _name + \\x22_fts) VALUES (\\x27delete-all\\x27);\\x22);\\n|' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zy. A batch does not remove the entry that a refresh wrote for a row that is gone before its batch (issue #271, review of pull request 291, B1): the row stays in the
#       index, and the removal at the end leaves it too.
expect_caught "a-batch-keeps-the-entry-of-a-row-that-is-gone" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_row_that_a_refresh_wrote_beside_the_sync_and_that_is_deleted_before_its_batch_is_gone_after_the_sync" \
  "sed -i 's/new\\[\\] { _source.BatchDropUnreachedFtsSql, _source.BatchDropUnreachedEntriesSql, /new[] { /' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zz. The removal takes the entries that a refresh wrote beside the sync (issue #271, ADR-0077): a row refreshed in a range that is done would go with the old generation.
expect_caught "the-removal-takes-the-entries-a-refresh-wrote" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_row_that_a_refresh_wrote_beside_the_sync_stays_when_the_old_generation_is_removed" \
  "sed -i 's/WHERE gen > -\$gen AND gen < \$gen ORDER BY/WHERE gen < \$gen ORDER BY/' src/Nordmann.LUI/Schema/LinkedSource.cs"

# 35zza. The sync does not give the write-ahead log back before a transaction (issue #271, review of pull request 291, B2): with another writer committing between the batches the log
#        is not started over and grows to the size of the whole index.
expect_caught "the-sync-does-not-give-the-log-back" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_write_ahead_log_is_not_the_whole_index_when_another_writer_commits_between_the_batches" \
  "sed -i '/^            ResetTheLog(writer);/d' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zzb. The sync leaves the busy timeout of its connection at 0 after its checkpoint (issue #271, ADR-0077 decision 13, review of pull request 291, round 2): the connection goes back to the pool
#        configured, and every later write of LUI on it fails at once under contention for the life of the pool.
expect_caught "the-sync-leaves-the-busy-timeout-at-zero" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_connection_of_a_sync_goes_back_to_the_pool_with_the_busy_timeout_of_the_catalog" \
  "sed -i 's/PRAGMA busy_timeout = {_database.BusyTimeoutMilliseconds};/PRAGMA busy_timeout = {_database.BusyTimeoutMilliseconds * 0};/' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zzc. The same mutation, seen from a write: a write of LUI after the sync fails after the retry of the provider instead of waiting for a lock that is held for a while.
expect_caught "a-write-after-the-sync-does-not-wait-for-the-lock" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.A_write_of_LUI_after_a_sync_waits_for_a_lock_that_is_held_for_a_while_and_does_not_fail_at_once" \
  "sed -i 's/PRAGMA busy_timeout = {_database.BusyTimeoutMilliseconds};/PRAGMA busy_timeout = {_database.BusyTimeoutMilliseconds * 0};/' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zzd. The checkpoint of the sync waits for a reader that holds a snapshot of the log (issue #271, ADR-0077 decision 13, review of pull request 291, round 3): it takes SQLite's writer lock and
#        waits the busy timeout in every transaction of the sync, and the writers of the application are locked out.
expect_caught "the-checkpoint-waits-for-a-reader" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.The_checkpoint_of_the_sync_does_not_wait_for_a_reader_that_holds_a_snapshot_of_the_log" \
  "sed -i '/Execute(writer, null, \"PRAGMA busy_timeout = 0;\");/d' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zze. LongestLock counts the time the sync waited for the lock of the application (issue #271, review of pull request 291, round 3, B2): it is no time that the sync held it.
expect_caught "longest-lock-counts-the-wait-for-the-lock" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~LinkedBatchSyncTests.LongestLock_does_not_count_the_time_the_sync_waited_for_the_lock_of_the_application" \
  "sed -i 's/var held = Stopwatch.StartNew();/var held = checkpoint;/' src/Nordmann.LUI/Indexes/LinkedSync.cs"

# 35zzf. A call by name that takes a stale snapshot for the truth (issue #273, ADR-0076): the index that another process created again with another definition is
#        a mismatch for a caller that declared no definition — a false exception, thrown from a snapshot that the file has left.
expect_caught "a-call-by-name-throws-a-mismatch-from-a-stale-snapshot" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.An_index_that_was_created_again_with_another_definition_is_used_as_it_is_stored_and_throws_no_mismatch" \
  "sed -i 's|LuiDefinitionMismatchException \&\& runtime.Guard.HasFailed)|LuiDefinitionMismatchException \&\& runtime.Guard.HasFailed \&\& exception is null)|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzg. Indexes() reads the definitions from the file at every call (issue #273, ADR-0076): the definition fields come from memory, and only a move of the
#        schema counter reads lui_meta again.
expect_caught "indexes-reads-the-definitions-from-the-file-at-every-call" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.Indexes_executes_no_SQL_for_the_definition_fields" \
  "sed -i 's|        _ = StoredIndexes.ReloadIfChanged();|        _ = StoredIndexes.Reload();|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzh. Indexes() lists the memory of the catalog without asking whether the file moved (issue #273, ADR-0076): an index that another process created or dropped is
#        not in the list — a stale list.
expect_caught "indexes-lists-a-stale-snapshot" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.Indexes_finds_an_index_that_another_catalog_created_after_the_open" \
  "sed -i 's|        _ = StoredIndexes.ReloadIfChanged();|        _ = StoredIndexes.Current;|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzi. Rebuild of a linked index is not refused (issue #273, ADR-0076): the cast of the runtime fails instead of the message of the issue.
expect_caught "rebuild-of-a-linked-index-is-not-refused" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.Rebuild_of_a_linked_index_throws_with_the_message" \
  "sed -i 's|            entry => string.Equals(entry.Source, LinkedSource.SourceName, StringComparison.Ordinal)|            entry => entry is null|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzj. The refusal of Rebuild is thrown from a snapshot that the file has not been asked about (issue #273, ADR-0076): a linked name that another process
#        created again as an embedded index is refused for what it was.
expect_caught "rebuild-is-refused-on-the-word-of-a-stale-snapshot" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_linked_name_that_another_catalog_created_again_as_an_embedded_index_is_rebuilt_and_not_refused" \
  "perl -0pi -e 's|                if \(StoredIndexes.Refresh\(snapshot\)\)\n                \{\n                    continue;\n                \}\n\n                throw refusal;|                throw refusal;|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzk. A missing name reloads the definitions twice (issue #273, ADR-0076): the name that a user types on an admin endpoint costs two reads of lui_meta,
#        and the memory of the missing name does not save the second one.
expect_caught "a-missing-name-reloads-twice" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_missing_name_asked_twice_reloads_once" \
  "perl -0pi -e 's|(FindStoredByName\(string name, out StoredIndexes.Snapshot snapshot\)\n    \{\n        while \(true\)\n        \{\n)|\$1            _ = StoredIndexes.Reload();\n|' src/Nordmann.LUI/LuiCatalog.cs"

# 35zzl. The state of a typo dictionary in the list is the one of a dictionary of another definition (issue #273, ADR-0076): the index was created again under the same name,
#        and the dictionary of the old definition is reported as the dictionary of the new one.
expect_caught "indexes-reports-the-dictionary-of-another-definition" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.The_dictionary_of_another_definition_is_not_the_dictionary_of_the_stored_one" \
  "sed -i 's|_byIndex.TryGetValue(indexName, out var entry) \&\& string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)|_byIndex.TryGetValue(indexName, out var entry)|' src/Nordmann.LUI/Typos/TypoDictionaries.cs"

# 35zzm. The runtime by name knows no payload columns (issue #273, review of pull request 292, B1): Check(name) of a linked index says healthy after the application dropped a column the payload is
#        built from.
expect_caught "check-by-name-ignores-the-payload-columns" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.Check_by_name_finds_a_payload_column_that_is_gone_from_the_table_without_a_map" \
  "sed -i 's|entry, counter, byName: true);|entry, counter);|' src/Nordmann.LUI/Indexes/IndexRuntimes.cs"

# 35zzn. A definition that is no JSON surfaces as the bare error of the parser (issue #273, review of pull request 292, B2): it names no index, and the operator of an admin endpoint cannot tell which one to drop.
expect_caught "a-malformed-definition-names-no-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_row_that_cannot_be_read_is_a_mismatch_that_names_the_index_on_every_path_and_spoils_no_other_index" \
  "sed -i 's|        catch (System.Text.Json.JsonException exception)\$|        catch (System.Text.Json.JsonException exception) when (json is null)|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zzo. A definition of 'null' is an InvalidOperationException that is no LuiException (issue #273, review of pull request 292, B2).
expect_caught "an-empty-definition-is-no-mismatch" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_row_that_cannot_be_read_is_a_mismatch_that_names_the_index_on_every_path_and_spoils_no_other_index" \
  "sed -i 's|throw new LuiDefinitionMismatchException(indexName, \"an empty definition\", \"a definition of this version\", advice)|throw new InvalidOperationException(\"empty\")|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zzp. The drop does not take a row it cannot read (issue #273, ADR-0076): the way out of a damaged row is closed.
expect_caught "the-drop-cannot-take-a-damaged-row" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_row_that_cannot_be_read_is_a_mismatch_that_names_the_index_on_every_path_and_spoils_no_other_index" \
  "sed -i 's|        catch (System.Text.Json.JsonException)\$|        catch (System.Text.Json.JsonException) when (indexName is null)|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zzq. A damaged row of a linked index is dropped as an embedded one (issue #273, review of pull request 292, round 2): the source column is not read, the outbox table stays and the next Create fails.
expect_caught "a-damaged-linked-row-is-dropped-as-an-embedded-index" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_damaged_row_of_a_linked_index_is_dropped_as_a_linked_index_and_its_triggers_go_only_with_the_flag" \
  "sed -i 's|        var source = stored?.Source ?? sourceName;|        var source = stored?.Source ?? EmbeddedSource.SourceName;|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 35zzr. The drop of a damaged linked row removes the triggers of the application without dropTriggers (issue #273, review of pull request 292, round 2).
expect_caught "a-damaged-linked-row-loses-its-triggers-without-the-flag" "${INTEGRATION_PROJECT}" \
  "FullyQualifiedName~MaintenanceByNameTests.A_damaged_row_of_a_linked_index_is_dropped_as_a_linked_index_and_its_triggers_go_only_with_the_flag" \
  "sed -i 's|        if (!dropTriggers \&\& string.Equals(source, LinkedSource.SourceName, StringComparison.Ordinal) \&\& HasTriggers|        if (false \&\& string.Equals(source, LinkedSource.SourceName, StringComparison.Ordinal) \&\& HasTriggers|' src/Nordmann.LUI/Indexes/IndexStore.cs"

# 36. The clean trees stay green.
expect_pass "clean-tree-architecture" "${ARCHITECTURE_PROJECT}" "FullyQualifiedName~RepositoryLayoutTests"
expect_pass "clean-tree-unit" "${UNIT_PROJECT}" "FullyQualifiedName~NormalizerTests"

if [ "${mutations_run}" -eq 0 ]; then
  if [ -n "${only}" ]; then
    echo "FAIL  no mutation is called ${only}"
  else
    echo "FAIL  the shard ${SHARD_INDEX}/${SHARD_COUNT} ran no mutation — the shards are more than the mutations"
  fi
  failures=$((failures + 1))
fi

echo "=== shard ${SHARD_INDEX}/${SHARD_COUNT}: ${mutations_run} of ${mutation_number} mutation(s), ${failures} failure(s) ==="
exit "${failures}"
