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
  "printf 'namespace Nordmann.LUI.Querying;\n\ninternal static class Probe\n{\n    internal static string Name(LuiCatalog catalog) => catalog.ConnectionString;\n}\n' > src/Nordmann.LUI/Querying/Probe.cs"

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
  "printf 'namespace Nordmann.LUI.Unit.Tests;\n\ninternal static class Probe\n{\n    internal static object Open() => LuiCatalog.Open(\"Data Source=probe.db\");\n}\n' > tests/Nordmann.LUI.Unit.Tests/Probe.cs"

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

# 15. The German transliteration of §6.1 changes — "ä" becomes "a" instead of
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
expect_caught "a-benchmark-report-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBenchmarksTests" \
  "sed -i '0,/ μs\*\* |/s// ms** |/' benchmarks/results/Nordmann.LUI.Benchmarks.QueryBenchmarks-report-github.md"

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

# 24b. The index knows no types (ADR-0035): the hit gets back a payload property — a type parameter's
#      view through the back door. It builds; only the guard of the public API can react.
expect_caught "the-hit-gets-a-payload-property" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_index_knows_no_types" \
  "sed -i 's|    /// <summary>Whether the entry has a payload.</summary>|    /// <summary>x</summary>\n    public object? Payload => StoredPayload;\n\n    /// <summary>Whether the entry has a payload.</summary>|' src/Nordmann.LUI/LuiHit.cs"

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

# 24e. A limit of the budget table of the README is edited by hand (issue #38): the limits are the targets of
#      §16, and a promise that drifts from the contract is a promise nobody checked. The guard reads the
#      specification and compares.
expect_caught "a-budget-limit-in-the-readme-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBudgetsTests" \
  "sed -i '0,/≤ 5 ms/s//≤ 6 ms/' README.md"

# 24f. A limit of the status table of the record is edited by hand (issue #38): the summary of the run promises
#      what the contract does not. The guard reads the specification and compares.
expect_caught "a-budget-limit-in-the-record-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~ReadmeBudgetsTests" \
  "sed -i '0,/≤ 20 ms/s//≤ 21 ms/' docs/benchmarks.md"

# 24g. A link of the documentation points at nothing (issue #66): the promise of the link check is that every
#      relative link of every markdown file names a file that exists. A link that points at nothing is a broken
#      promise, and the guard `LinkCheckTests` has to name it.
expect_caught "a-link-in-the-documentation-points-at-nothing" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~LinkCheckTests" \
  "echo '[a link that points at nothing](./this-file-does-not-exist.md)' >> docs/benchmarks.md"

# 25. The concurrency of the round of issue #67: the write-ahead log is switched off. Without it a reader beside a
#      writer waits for the lock and gives up with an error — the promise of §15 (many readers beside one writer)
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
#      commit cannot. The guard of the workflows has to name it.
expect_caught "an-action-pinned-by-its-tag" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_action_is_pinned_by_its_commit_and_names_its_version" \
  "sed -i '0,/actions\/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0/s//actions\/checkout@v4/' .github/workflows/ci.yml"

# 26e. A job loses its timeout (issue #74, ADR-0047): the default of GitHub is six hours of a hanging test.
expect_caught "a-job-without-a-timeout" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Every_job_has_a_timeout" \
  "sed -i '0,/    timeout-minutes: 10/{/    timeout-minutes: 10/d}' .github/workflows/ci.yml"

# 26f. The SDK of global.json becomes a channel (review of pull request 83): setup-dotnet turns "10.0.x" into the channel
#      10.0 and installs the latest 10.0 of the image — the pin is gone. The guard of the SDK has to name it.
expect_caught "the-sdk-version-is-a-channel" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~The_sdk_is_the_one_of_global_json" \
  "sed -i 's/\"version\": \"10.0.114\"/\"version\": \"10.0.x\"/' global.json"

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

# 27. The clean trees stay green.
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
