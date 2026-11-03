#!/usr/bin/env bash
#
# mutation-check.sh — prove that the architecture guards actually bite.
#
# A green test proves nothing until it has been seen red. Every rule in
# Nordmann.LUI.Architecture.Tests is mutated here in an isolated copy of the
# repository, and the guard must react.
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
set -uo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly WORK="$(mktemp -d)"

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

# expect_caught <name> <test-filter> <mutation-command>
expect_caught() {
  local name="$1"
  local filter="$2"
  local mutation="$3"
  local copy
  copy="$(prepare_copy "${name}")"

  ( cd "${copy}" && eval "${mutation}" ) >/dev/null 2>&1

  local output
  output="$( cd "${copy}" && dotnet test tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj \
      --filter "${filter}" --nologo 2>&1 )"

  if echo "${output}" | grep -q "Failed!"; then
    echo "PASS  ${name}: CAUGHT BY TEST"
  elif echo "${output}" | grep -qE "error (NU|MSB|CS)[0-9]+"; then
    echo "PASS  ${name}: REFUSED BY BUILD"
  else
    echo "FAIL  ${name}: nothing reacted to the mutation"
    echo "${output}" | tail -6
    failures=$((failures + 1))
  fi
}

# expect_pass <name> <test-filter>
expect_pass() {
  local name="$1"
  local filter="$2"
  local copy
  copy="$(prepare_copy "${name}")"

  local output
  output="$( cd "${copy}" && dotnet test tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj \
      --filter "${filter}" --nologo 2>&1 )"

  if echo "${output}" | grep -q "Passed!"; then
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
if ! ( cd "${preflight}" && dotnet test tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj \
    --filter "FullyQualifiedName~Repository_Layout" --nologo 2>&1 ) | grep -q "Passed!"; then
  echo "ABORT the architecture tests do not pass on the clean tree — fix the environment first"
  echo "      (dotnet on the PATH, a writable DOTNET_CLI_HOME: ${DOTNET_CLI_HOME}, packages restorable)"
  exit 2
fi

# 1. A package version sneaks into a project file.
expect_caught "package-version-in-csproj" \
  "No_PackageReference_carries_a_Version" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" Version=\"6.7.3\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 2. A package without a central version. Central package management refuses
#    this at restore time — the toolchain is the guard here, the test is the
#    second line.
expect_caught "package-without-central-version" \
  "Every_PackageReference_has_a_Central_PackageVersion" \
  "sed -i 's|<PackageReference Include=\"NetArchTest.Rules\" />|<PackageReference Include=\"NetArchTest.Rules\" /><PackageReference Include=\"Serilog\" />|' tests/Nordmann.LUI.Architecture.Tests/Nordmann.LUI.Architecture.Tests.csproj"

# 3. The sample becomes packable.
expect_caught "sample-is-packable" \
  "Only_the_library_project_is_packable" \
  "sed -i 's|<IsPackable>false</IsPackable>|<IsPackable>true</IsPackable>|' samples/Lui.Sample/Lui.Sample.csproj"

# 4. The library reaches for another project. The stub has no reference back,
#    so the build stays valid and the test has to be the one that reacts.
expect_caught "library-references-another-project" \
  "The_library_project_references_no_other_project" \
  "mkdir -p tests/Stub && printf '<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' > tests/Stub/Stub.csproj && sed -i 's|</Project>|  <ItemGroup><ProjectReference Include=\"../../tests/Stub/Stub.csproj\" /></ItemGroup></Project>|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 5. An undocumented dependency — with its central version, so the build
#    stays valid and the *test* has to be the one that reacts.
expect_caught "undocumented-dependency" \
  "The_library_references_only_the_documented_dependencies" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" /><PackageReference Include=\"Serilog\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj && sed -i 's|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" />|<PackageVersion Include=\"symspell\" Version=\"6.7.3\" /><PackageVersion Include=\"Serilog\" Version=\"4.3.1\" />|' Directory.Packages.props"

# 6. A source file in the wrong namespace.
expect_caught "namespace-folder-mismatch" \
  "Every_source_file_declares_the_namespace_of_its_folder" \
  "mkdir -p src/Nordmann.LUI/Schema && printf 'namespace Nordmann.LUI.Wrong;\n\ninternal static class Probe\n{\n}\n' > src/Nordmann.LUI/Schema/Probe.cs"

# 7. The licence or the readme of the package changes.
expect_caught "package-readme-missing" \
  "The_library_ships_the_readme_and_the_mit_license" \
  "sed -i 's|<PackageReadmeFile>README.md</PackageReadmeFile>|<PackageReadmeFile>Handbook.md</PackageReadmeFile>|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 8. The query path reaches up to the catalog — the named exception of ADR-0014 is gone (review DevEx D4, PR-007).
expect_caught "query-path-uses-the-catalog" \
  "No_layer_goes_up_through_the_facade" \
  "printf 'namespace Nordmann.LUI.Querying;\n\ninternal static class Probe\n{\n    internal static string Name(LuiCatalog catalog) => catalog.ConnectionString;\n}\n' > src/Nordmann.LUI/Querying/Probe.cs"

# 9. The connections reach up to the schema: Storage is the bottom layer.
expect_caught "storage-knows-the-schema" \
  "A_namespace_knows_only_the_ones_below_it" \
  "printf 'namespace Nordmann.LUI.Storage;\n\ninternal static class Probe\n{\n    internal static System.Type Type => typeof(Nordmann.LUI.Schema.RowId);\n}\n' > src/Nordmann.LUI/Storage/Probe.cs"

# 10. The core takes a dependency of the hosting (ADR-0018): the console application would carry it.
expect_caught "core-references-the-hosting-abstractions" \
  "The_library_ships_only_the_documented_runtime_dependencies" \
  "sed -i 's|<PackageReference Include=\"symspell\" />|<PackageReference Include=\"symspell\" /><PackageReference Include=\"Microsoft.Extensions.DependencyInjection.Abstractions\" />|' src/Nordmann.LUI/Nordmann.LUI.csproj"

# 11. The clean tree stays green.
expect_pass "clean-tree" "FullyQualifiedName~Repository_Layout"

echo "=== ${failures} failure(s) ==="
exit "${failures}"
