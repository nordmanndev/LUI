#!/usr/bin/env python3
"""tutorial-check.py - the C# blocks of the tutorial are compiled, run and compared with the page.

The tutorial promises that every code block compiles (docs/tutorial/README.md). Nothing
held that promise up: a block that names an API the library does not have any more, or a
page that promises an output the code does not print, stayed on the page until a reader
noticed it.

The check reads every page under docs/tutorial/, compiles the C# blocks of the page as one
program, runs it and compares what it prints with the text block of the page. A block that
does not compile, or a page that says something else than the code does, fails the check -
and the message names the page and the block.

The pages are compiled against the library of the repository (the source, not the package):
the promise of the tutorial is about the API, and the API is the source.

Two kinds of block are special, and both are written down in the pages themselves:

  - a block that continues the block above it says so in its first line
    ("// continues the block above"). Its statements are compiled in the scope of the
    block above, so that it sees what that block declared. The marker is the same kind of
    convention the smoke test of the README uses for the types of the application.
  - a block that declares no statement at all is a listing of the types of the application.
    It is compiled on its own - it is not a program, and the program of the page has these
    types already.

Usage:
  tools/tutorial-check.py [--keep]

The exit code decides: 0 when every page holds its promise, 1 when one does not.
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TUTORIAL = ROOT / "docs" / "tutorial"
LIBRARY = ROOT / "src" / "Nordmann.LUI" / "Nordmann.LUI.csproj"

# The pages the check does not compile, and why. Every other page of the tutorial is compiled:
# a new page is a decision and not an accident, and a page that is skipped here is named.
SKIPPED = {
    "05-in-the-application.md": "it needs a host - a web application or a container; its blocks are not console programs",
}

# The first line of a block that continues the block above it.
CONTINUES = "// continues the block above"

# A line at the left margin that begins a type declaration. Everything from there to the
# end of the block belongs to the type of the application; the rest are its statements.
TYPE_START = re.compile(
    r"^(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|record|class|struct|interface|enum|delegate|file)\b"
    r"|^\[[A-Za-z_][A-Za-z0-9_.]*(?:\(.*\))?\]\s*$"
)

# A using directive of a block. They go to the top of the file the block is compiled in.
USING = re.compile(r"^using [A-Za-z0-9_.]+;\s*$")

# The name of a block in the output of the program: the marker between the output of one
# block and the output of the next.
MARKER = re.compile(r"^<<<block (\d+)>>>$")


class Block:
    """One C# block of a page: its statements, its types and what the page says it prints."""

    def __init__(self, number: int, lines: list[str]) -> None:
        self.number = number
        self.usings: list[str] = []
        self.statements: list[str] = []
        self.types: list[str] = []
        self.expected: str | None = None
        self.continues = False

        self._split(lines)

    def _split(self, lines: list[str]) -> None:
        rest = []
        for line in lines:
            if USING.match(line):
                if line not in self.usings:
                    self.usings.append(line)
            else:
                rest.append(line)

        if rest and rest[0].strip() == CONTINUES:
            self.continues = True
            rest = rest[1:]

        start = next((index for index, line in enumerate(rest) if TYPE_START.match(line)), None)
        if start is None:
            self.statements = rest
            return

        # A doc comment belongs to the type below it.
        while start > 0 and rest[start - 1].lstrip().startswith("///"):
            start -= 1

        self.statements = rest[:start]
        self.types = rest[start:]

    def merge(self, other: "Block") -> None:
        """The statements of a block that continues this one are compiled in its scope."""
        self.statements = self.statements + [f'Console.WriteLine("<<<block {other.number}>>>");'] + other.statements
        self.types = self.types + other.types
        for using in other.usings:
            if using not in self.usings:
                self.usings.append(using)

    @property
    def has_statements(self) -> bool:
        """Whether the block has something to run. A blank line is nothing."""
        return any(line.strip() for line in self.statements)


class Compilation:
    """The blocks of a page that are compiled together: one program, one build."""

    def __init__(self, blocks: list[Block]) -> None:
        self.blocks = blocks

    @property
    def runs(self) -> bool:
        """Whether the compilation has something to run: a block without a statement has not."""
        return any(block.has_statements for block in self.blocks)


def fenced_blocks(text: str) -> list[tuple[str, list[str]]]:
    """The fenced blocks of a markdown file: the language and the lines of the block."""
    blocks: list[tuple[str, list[str]]] = []
    language: str | None = None
    lines: list[str] = []

    for line in text.replace("\r\n", "\n").split("\n"):
        if language is None:
            if line.startswith("```"):
                language = line[3:].strip()
                lines = []
            continue

        if line.startswith("```"):
            blocks.append((language, lines))
            language = None
            continue

        lines.append(line)

    return blocks


def blocks_of_page(path: Path) -> list[Block]:
    """The C# blocks of a page, each with the output the page promises for it."""
    blocks: list[Block] = []
    pending: Block | None = None

    for language, lines in fenced_blocks(path.read_text(encoding="utf-8")):
        if language == "csharp":
            pending = Block(len(blocks) + 1, lines)
            blocks.append(pending)
        elif language == "text" and pending is not None and pending.expected is None:
            # The text block after a C# block is what that block has to print.
            pending.expected = "\n".join(lines)

    return blocks


def compilations_of(blocks: list[Block]) -> list[Compilation]:
    """The compilations of a page: the blocks in the order of the page."""
    compilations: list[Compilation] = []
    pending: list[Block] = []

    for block in blocks:
        if block.continues:
            if not pending:
                raise ValueError(f"block {block.number} continues a block that is not there")
            pending[-1].merge(block)
        elif not block.has_statements:
            # A listing of the types of the application: it is compiled on its own.
            if pending:
                compilations.append(Compilation(pending))
                pending = []
            compilations.append(Compilation([block]))
        else:
            pending.append(block)

    if pending:
        compilations.append(Compilation(pending))

    return compilations


def namespace_of(page: Path, number: int) -> str:
    """The namespace of a block: the page and the number of the block."""
    stem = re.sub(r"[^A-Za-z0-9_]", "_", page.stem)
    return f"Page{stem}Block{number:02d}"


def indent(lines: list[str]) -> list[str]:
    """The lines one level deeper. Every line moves, so a raw string keeps its shape."""
    return [f"    {line}" if line.strip() else line for line in lines]


def write_compilation(project: Path, page: Path, compilation: Compilation) -> None:
    """The files of one compilation: a namespace per block and the program that runs them."""
    blocks = project / "Blocks"
    blocks.mkdir(parents=True, exist_ok=True)
    for stale in blocks.glob("*.cs"):
        stale.unlink()

    calls = []
    for block in compilation.blocks:
        namespace = namespace_of(page, block.number)
        lines = [
            *block.usings,
            "",
            f"namespace {namespace};",
            "",
            "internal static class Runner",
            "{",
            "    internal static void Run()",
            "    {",
            *indent(block.statements),
            "    }",
            "}",
            "",
            *block.types,
        ]
        (blocks / f"{namespace}.cs").write_text("\n".join(lines) + "\n", encoding="utf-8")
        calls.append(f'            ("block {block.number}", {namespace}.Runner.Run),')

    program = f"""using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// UTF-8 whatever the console of the machine is: the page and the output are UTF-8.
Console.OutputEncoding = Encoding.UTF8;

var blocks = new (string Name, Action Block)[]
{{
{chr(10).join(calls)}
}};

var real = Console.Out;
foreach (var (name, block) in blocks)
{{
    var buffer = new StringWriter();
    Console.SetOut(buffer);
    try
    {{
        Console.WriteLine($"<<<{{name}}>>>");
        block();
    }}
    finally
    {{
        Console.SetOut(real);
    }}

    Console.Write(buffer.ToString());
}}

return 0;
"""
    (project / "Program.cs").write_text(program, encoding="utf-8")


def write_project(work: Path) -> Path:
    """The project that holds the blocks: the library of the repository and SQLite."""
    project = work / "TutorialCheck"
    project.mkdir(parents=True, exist_ok=True)

    packages = (ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
    version = re.search(r'<PackageVersion Include="Microsoft.Data.Sqlite" Version="([^"]+)"', packages)
    if version is None:
        raise SystemExit("tutorial-check: the version of Microsoft.Data.Sqlite is not in Directory.Packages.props")

    # The target framework is the one of the repository — the project stands outside of it and
    # inherits nothing, so it has to be told.
    properties = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    framework = re.search(r"<TargetFramework>([^<]+)</TargetFramework>", properties)
    if framework is None:
        raise SystemExit("tutorial-check: the target framework is not in Directory.Build.props")

    project_file = f"""<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{framework.group(1)}</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{LIBRARY.as_posix()}" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="{version.group(1)}" />
  </ItemGroup>

</Project>
"""
    (project / "TutorialCheck.csproj").write_text(project_file, encoding="utf-8")
    return project


def build(project: Path) -> subprocess.CompletedProcess:
    return subprocess.run(
        ["dotnet", "build", str(project / "TutorialCheck.csproj"), "--configuration", "Release", "--nologo"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=str(project),
    )


def run(project: Path, directory: Path, page: Path) -> subprocess.CompletedProcess:
    """The program of a page, in the folder of the page: the database of the page is its own."""
    directory.mkdir(parents=True, exist_ok=True)
    assemblies = sorted((project / "bin" / "Release").glob("*/TutorialCheck.dll"))
    if not assemblies:
        raise SystemExit("tutorial-check: the program of the pages was not built")
    return subprocess.run(
        ["dotnet", str(assemblies[0]), page.stem],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=str(directory),
    )


def printed(output: str) -> dict[int, str]:
    """What every block printed, between the markers."""
    result: dict[int, str] = {}
    number: int | None = None
    lines: list[str] = []

    for line in output.replace("\r\n", "\n").split("\n"):
        marker = MARKER.match(line)
        if marker:
            if number is not None:
                result[number] = "\n".join(lines)
            number = int(marker.group(1))
            lines = []
            continue

        if number is not None:
            lines.append(line)

    if number is not None:
        result[number] = "\n".join(lines)

    return result


def same(expected: str | None, actual: str) -> bool:
    """Whether the block prints what the page says. A block without a text block prints nothing."""
    if expected is None:
        return actual.strip() == ""

    return expected.rstrip("\n") == actual.rstrip("\n")


def report(page: Path, block: Block, actual: str) -> str:
    expected = block.expected or "(nothing)"
    return (
        f"{page.relative_to(ROOT).as_posix()}, block {block.number}: the output is not what the page says\n"
        f"  the page says:\n{indent_text(expected)}\n"
        f"  the block prints:\n{indent_text(actual or '(nothing)')}"
    )


def compile_errors(page: Path, output: str) -> list[str]:
    """The errors of the build, each with the block it belongs to."""
    problems = []
    seen = set()

    for line in output.split("\n"):
        if "error" not in line:
            continue

        text = line.strip()
        if text in seen:
            # The build writes its errors twice — to its output and to its error stream.
            continue
        seen.add(text)

        # The file of the block carries its number: Page01_first_indexBlock02.cs
        found = re.search(r"Block(\d+)\.cs", text)
        number = int(found.group(1)) if found else 0
        problems.append(
            f"{page.relative_to(ROOT).as_posix()}, block {number}: the block does not compile\n"
            f"  {text}"
        )

    return problems


def indent_text(text: str) -> str:
    return "\n".join(f"    {line}" for line in text.split("\n"))


def main() -> int:
    # The pages and the output of the program are UTF-8; the console of the machine is not necessarily.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    pages: dict[Path, list[Block]] = {}
    problems: list[str] = []

    for path in sorted(TUTORIAL.glob("*.md")):
        if path.name == "README.md":
            continue

        if path.name in SKIPPED:
            print(f"tutorial-check: skipped {path.relative_to(ROOT).as_posix()} - {SKIPPED[path.name]}")
            continue

        pages[path] = blocks_of_page(path)

    keep = "--keep" in sys.argv
    temporary = Path(tempfile.mkdtemp())
    if keep:
        print(f"tutorial-check: the work is in {temporary}")

    try:
        project = write_project(temporary)

        for page, blocks in pages.items():
            for compilation in compilations_of(blocks):
                write_compilation(project, page, compilation)

                built = build(project)
                if built.returncode != 0:
                    problems.extend(compile_errors(page, built.stdout + built.stderr))
                    continue

                if not compilation.runs:
                    continue

                result = run(project, temporary / "pages" / page.stem, page)
                if result.returncode != 0:
                    problems.append(
                        f"{page.relative_to(ROOT).as_posix()}: the program of the page failed\n{result.stderr.strip()}"
                    )
                    continue

                outputs = printed(result.stdout)
                for block in compilation.blocks:
                    actual = outputs.get(block.number, "")
                    if not same(block.expected, actual):
                        problems.append(report(page, block, actual))
    finally:
        if not keep:
            shutil.rmtree(temporary, ignore_errors=True)

    if problems:
        print("tutorial-check: the tutorial does not say what it does")
        for problem in problems:
            print(problem)
        return 1

    print(f"tutorial-check: {len(pages)} page(s) checked, every block compiles and prints what the page says")
    return 0


if __name__ == "__main__":
    sys.exit(main())
