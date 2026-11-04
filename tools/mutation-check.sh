#!/usr/bin/env bash
#
# mutation-check.sh — prove that the guards actually bite.
#
# A green test proves nothing until it has been seen red. Every rule that the
# test suite holds up is mutated here in an isolated copy of the repository,
# and the guard must react.
#
# Two mechanisms can catch a mutation, and the script reports which one did:
#
#   CAUGHT BY TEST   the test ran and failed  → the rule is a real test
#   REFUSED BY BUILD the build itself rejected it → the rule is enforced by
#                   the toolchain (central package management, for example)
#
# Both are wins, but they are different wins, and a mutation that is caught
# for the wrong reason is worth nothing. Each mutation runs on its own copy:
# two runs against the same files destroy each other's evidence.
#
# The architecture rules are mutated in the architecture project, the rules of
# the library itself in the unit project — the fast one, because it needs no
# database and a mutation that takes 40 seconds is a mutation nobody runs.
#
set -uo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly WORK="$(mktemp -d)"

readonly ARCHITECTURE_PROJECT="tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj"
readonly UNIT_PROJECT="tests/Nordmann.LUI.Unit.Tests/Nordmann.LUI.Unit.Tests.csproj"

cleanup() { rm -rf "${WORK}"; }
trap cleanup EXIT

export HOME="${HOME:-/root}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${HOME}/.dotnet-home}"
export DOTNET_NOLOGO=1
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
    echo "PASS  ${name}: REFUSED BY BUILD"
  else
    echo "FAIL  ${name}: nothing reacted to the mutation"
    echo "${output}" | tail -6
    failures=$((failures + 1))
  fi
}

# expect_pass <name> <project-file> <test-filter>
expect_pass() {
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

echo "=== mutation check ==="

# 0. Pre-flight: the guards are green on the clean tree. When they are not, every mutation below would "fail" for
#    the same reason — an environment that cannot run dotnet test — and the report would be ten false alarms.
preflight="$(prepare_copy "preflight")"
if ! ( cd "${preflight}" && dotnet test "${ARCHITECTURE_PROJECT}" \
    --filter "FullyQualifiedName~Repository_Layout" --nologo 2>&1 ) | grep -q "Passed!"; then
  echo "ABORT the architecture tests do not pass on the clean tree — fix the environment first"
  echo "      (dotnet on the PATH, a writable DOTNET_CLI_HOME: ${DOTNET_CLI_HOME}, packages restorable)"
  exit 2
fi

# 1. A package version sneaks into a project file.
expect_caught "package-version-in-csproj" "${ARCHITECTURE_PROJECT}" \
  "No_PackageReference_carries_a_Version" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" Version=\"6.7.3\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 2. A package without a central version. Central package management refuses
#    this at restore time — the toolchain is the guard here, the test is the
#    second line.
expect_caught "package-without-central-version" "${ARCHITECTURE_PROJECT}" \
  "Every_PackageReference_has_a_Central_PackageVersion" \
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

# 16. The typo dictionary forgets a mark of a sync that came during its build
#     (review of pull request 34, I1/I2): the publish takes the count after the
#     table was read, not before — a stale dictionary would say Current for good.
expect_caught "a-build-forgets-the-marks-during-it" "${UNIT_PROJECT}" \
  "FullyQualifiedName~TypoDictionaryStateTests" \
  "sed -i 's|Volatile.Write(ref _builtAt, marks);|Volatile.Write(ref _builtAt, Volatile.Read(ref _marks));|' src/Nordmann.LUI/Typos/TypoDictionary.cs"

# 17. A benchmark number in the README is edited by hand (ADR-0022): the
#     tables are rendered from benchmarks/results/, and the guard notices.
expect_caught "a-readme-benchmark-number-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Readme_Benchmarks" \
  "sed -i '0,/ μs |/s// ms |/' README.md"

# 18. A number in a committed BenchmarkDotNet report is edited by hand, and the
#     README is not rendered anew (review of pull request 37, M2): the two no
#     longer agree, and the guard notices from the other side.
expect_caught "a-benchmark-report-edited-by-hand" "${ARCHITECTURE_PROJECT}" \
  "FullyQualifiedName~Readme_Benchmarks" \
  "sed -i '0,/ μs\*\* |/s// ms** |/' benchmarks/results/Nordmann.LUI.Benchmarks.QueryBenchmarks-report-github.md"

# 19. The clean trees stay green.
expect_pass "clean-tree-architecture" "${ARCHITECTURE_PROJECT}" "FullyQualifiedName~Repository_Layout"
expect_pass "clean-tree-unit" "${UNIT_PROJECT}" "FullyQualifiedName~NormalizerTests"

echo "=== ${failures} failure(s) ==="
exit "${failures}"
