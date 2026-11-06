#!/usr/bin/env bash
#
# package-smoke-test.sh — pack Nordmann.LUI and Nordmann.LUI.Hosting, install
# the .nupkgs into a throwaway console application from a local feed, and run it.
#
# The two packages are released together, with one version (ADR-0018): the
# hosting package must pin the core of the same version exactly ([x.y.z],
# #24), and the core must not carry the Microsoft.Extensions abstractions of
# the hosting.
#
# A package that cannot be consumed is not a package. This is the test that
# catches the three classic failures:
#   1. the .nupkg does not contain what the README tells people to use
#   2. a dependency is missing from the nuspec, so the restore fails
#   3. the assembly loads but its identity is not what the package claims
#
# It also runs every C# block of the README, in order, as one method of the
# smoke application: a README that shows an API the package does not have
# fails here (AGENTS.md).
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

echo "package-smoke-test: packing Nordmann.LUI and Nordmann.LUI.Hosting ${VERSION}"
rm -rf "${FEED}"
for project in Nordmann.LUI Nordmann.LUI.Hosting; do
  dotnet pack "${ROOT}/src/${project}/${project}.csproj" \
    --configuration Release \
    --output "${FEED}" \
    --nologo \
    -p:PackageVersion="${VERSION}"

  if [ ! -f "${FEED}/${project}.${VERSION}.nupkg" ]; then
    echo "package-smoke-test: ${FEED}/${project}.${VERSION}.nupkg was not produced" >&2
    exit 1
  fi
done

# the dependencies of the two packages, as a consumer's restore sees them
readonly CORE_NUSPEC="$(unzip -p "${FEED}/Nordmann.LUI.${VERSION}.nupkg" Nordmann.LUI.nuspec)"
readonly HOSTING_NUSPEC="$(unzip -p "${FEED}/Nordmann.LUI.Hosting.${VERSION}.nupkg" Nordmann.LUI.Hosting.nuspec)"
if grep -q 'id="Microsoft.Extensions' <<<"${CORE_NUSPEC}"; then
  echo "package-smoke-test: the core package depends on Microsoft.Extensions — that belongs to Nordmann.LUI.Hosting" >&2
  exit 1
fi
# exactly the core of the same version: [x.y.z], not >= x.y.z (#24, ADR-0018)
if ! grep -q "id=\"Nordmann.LUI\" version=\"\[${VERSION}\]\"" <<<"${HOSTING_NUSPEC}"; then
  echo "package-smoke-test: Nordmann.LUI.Hosting does not pin Nordmann.LUI exactly at [${VERSION}]" >&2
  grep "id=\"Nordmann.LUI\"" <<<"${HOSTING_NUSPEC}" >&2 || true
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

# The C# blocks of the README: their `using` lines go to the top of the file,
# the rest becomes the body of Readme(), in the order of the README.
# A block whose first line is "// The types of the application…" declares types: it goes after the statements, where
# C# allows a type, and the other blocks run in order inside one method.
readme_blocks() {
  awk -v want="$1" '
    /^```csharp$/ { inside = 1; n = 0; next }
    /^```$/ && inside {
      kind = (n > 0 && lines[1] ~ /^\/\/ The types of the application/) ? "types" : "body"
      if (want == "all" || want == kind) { for (i = 1; i <= n; i++) print lines[i] }
      inside = 0; next
    }
    inside { lines[++n] = $0 }' "${ROOT}/README.md"
}
readonly README_USINGS="$(readme_blocks all | grep -E '^using [A-Za-z0-9_.]+;$' | sort -u || true)"
readonly README_BODY="$(readme_blocks body | grep -vE '^using [A-Za-z0-9_.]+;$' | sed 's/^/    /' || true)"
readonly README_TYPES="$(readme_blocks types | grep -vE '^using [A-Za-z0-9_.]+;$' || true)"
if [ -z "${README_BODY//[[:space:]]/}" ]; then
  echo "package-smoke-test: the README has no C# block to run" >&2
  exit 1
fi

{
  echo "using System.Reflection;"
  echo "using Nordmann.LUI;"
  echo "${README_USINGS}" | grep -vx "using Nordmann.LUI;" || true
  cat <<'CS'

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

// The package has to carry the public API, not just an empty shell.
var catalogType = assembly.GetType("Nordmann.LUI.LuiCatalog");

if (catalogType is not { IsPublic: true })
{
    Console.Error.WriteLine("the package does not contain the public Nordmann.LUI.LuiCatalog");
    return 1;
}

Console.WriteLine($"found {catalogType.FullName}");

// And it has to work: a catalog on a file, an index, three entries, two questions.
var path = Path.Combine(Path.GetTempPath(), $"lui-smoke-{Environment.ProcessId}.db");
try
{
    using (var catalog = LuiCatalog.Open($"Data Source={path}"))
    {
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Apfelkuchen", weight: 10);
        tags.Upsert(2, "Bratapfelkuchen", weight: 1000);
        tags.Upsert(3, "vegan", weight: 5, priority: LuiPriority.System);

        var first = tags.Query("apfelkuch").Execute().Hits.FirstOrDefault().Text;
        var system = tags.Query("gan").Execute().Hits.FirstOrDefault().Text;
        Console.WriteLine($"apfelkuch -> {first}; gan -> {system}");

        // and a multi-word index: the order of the words and the spaces do not matter
        var titles = catalog.Create("titles", index => index.MultiWord().TypoTolerance());
        titles.Upsert(1, "Apfel Kuchen", weight: 10);
        titles.Upsert(2, "Bratapfelkuchen", weight: 1000);

        var reordered = titles.Query("kuchen apfel").Execute().Hits.FirstOrDefault().Text;
        var compact = titles.Query("apfelkuch").Execute().Hits.FirstOrDefault().Text;
        Console.WriteLine($"kuchen apfel -> {reordered}; apfelkuch -> {compact}");

        // and a typo
        var typo = titles.Query("apfle kuchen").Execute();
        Console.WriteLine($"apfle kuchen -> {typo.CorrectedQuery} ({typo.Hits.Count} hits)");

        if (first != "Apfelkuchen" || system != "vegan" || reordered != "Apfel Kuchen" || compact != "Apfel Kuchen"
            || typo.CorrectedQuery != "apfel kuchen" || typo.Hits.Count != 2)
        {
            Console.Error.WriteLine("the packed library answered a query wrongly");
            return 1;
        }
    }
}
finally
{
    foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
    {
        File.Delete(path + suffix);
    }
}

// And the README has to be true: its C# blocks, in order.
Console.WriteLine("running the C# blocks of the README");
Readme();
return 0;

static void Readme()
{
CS
  echo "${README_BODY}"
  echo "}"
  echo
  echo "${README_TYPES}"
} > "${SMOKE}/Program.cs"

echo "package-smoke-test: installing the packages from the local feed"
for package in Nordmann.LUI Nordmann.LUI.Hosting; do
  dotnet add "${SMOKE}/SmokeTest.csproj" package "${package}" \
    --version "${VERSION}" \
    --no-restore >/dev/null
done

# the README's host block needs a host — the application brings it, the package only its abstractions
HOSTING_VERSION="$(sed -n 's|.*<PackageVersion Include="Microsoft.Extensions.Hosting" Version="\([^"]*\)".*|\1|p' "${ROOT}/Directory.Packages.props")"
dotnet add "${SMOKE}/SmokeTest.csproj" package Microsoft.Extensions.Hosting \
  --version "${HOSTING_VERSION}" \
  --no-restore >/dev/null

dotnet restore "${SMOKE}/SmokeTest.csproj" --nologo >/dev/null

echo "package-smoke-test: running the smoke application"
# in its own folder: the README's database (app.db) lands there and goes with it
OUTPUT="$(cd "${SMOKE}" && dotnet run --project "${SMOKE}/SmokeTest.csproj" --no-restore --nologo)"
echo "${OUTPUT}"

# The comments of the quick start say what it prints; they have to stay true.
for expected in "Gedeckter Apfelkuchen (9000)" "Bratapfelkuchen (800)" "Gedeckter Apfelkuchen" "gedeckter" "#3a3" "vegan" "Vegan" "Current"; do
  if ! grep -qxF "${expected}" <<<"${OUTPUT}"; then
    echo "package-smoke-test: the README's quick start did not print '${expected}'" >&2
    exit 1
  fi
done

echo "package-smoke-test: OK"
