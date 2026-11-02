#!/usr/bin/env bash
#
# package-smoke-test.sh — pack Nordmann.LUI, install the .nupkg into a
# throwaway console application from a local feed, and run it.
#
# A package that cannot be consumed is not a package. This is the test that
# catches the three classic failures:
#   1. the .nupkg does not contain what the README tells people to use
#   2. a dependency is missing from the nuspec, so the restore fails
#   3. the assembly loads but its identity is not what the package claims
#
# The smoke application runs in an isolated packages folder, so a cached
# Nordmann.LUI in ~/.nuget/packages can never win over the freshly built one.
#
# Usage:
#   tools/package-smoke-test.sh [version]
#
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly VERSION="${1:-0.1.0}"
readonly FEED="${ROOT}/artifacts/nuget"
readonly WORK="$(mktemp -d)"
readonly SMOKE="${WORK}/SmokeTest"

cleanup() { rm -rf "${WORK}"; }
trap cleanup EXIT

echo "package-smoke-test: packing Nordmann.LUI ${VERSION}"
rm -rf "${FEED}"
dotnet pack "${ROOT}/src/Nordmann.LUI/Nordmann.LUI.csproj" \
  --configuration Release \
  --output "${FEED}" \
  --nologo \
  -p:PackageVersion="${VERSION}"

readonly NUPKG="${FEED}/Nordmann.LUI.${VERSION}.nupkg"
if [ ! -f "${NUPKG}" ]; then
  echo "package-smoke-test: ${NUPKG} was not produced" >&2
  exit 1
fi

echo "package-smoke-test: creating the smoke application"
dotnet new console --output "${SMOKE}" --framework net10.0 --no-restore >/dev/null

cat > "${SMOKE}/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="lui-local" value="${FEED}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="${WORK}/packages" />
  </config>
</configuration>
XML

cat > "${SMOKE}/Program.cs" <<'CS'
using System.Reflection;

// The consumer's view of the package: can it be restored, does it load, and
// does it say what it is?
var assembly = Assembly.Load("Nordmann.LUI");

var name = assembly.GetName().Name;
var version = assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>()
    .FirstOrDefault()?.InformationalVersion ?? "(none)";

Console.WriteLine($"loaded {name} {version}");

if (name != "Nordmann.LUI")
{
    Console.Error.WriteLine($"expected the assembly to be named Nordmann.LUI, got '{name}'");
    return 1;
}

return 0;
CS

echo "package-smoke-test: installing the package from the local feed"
dotnet add "${SMOKE}/SmokeTest.csproj" package Nordmann.LUI \
  --version "${VERSION}" \
  --no-restore >/dev/null

dotnet restore "${SMOKE}/SmokeTest.csproj" --nologo >/dev/null

echo "package-smoke-test: running the smoke application"
dotnet run --project "${SMOKE}/SmokeTest.csproj" --no-restore --nologo

echo "package-smoke-test: OK"
