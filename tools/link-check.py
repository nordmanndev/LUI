#!/usr/bin/env python3
"""Check every relative link and image of the markdown files of the repository.

    python3 tools/link-check.py

A link that points at a file that does not exist, or at an anchor that the file
does not have, is a broken promise in the documentation. The check walks every
markdown file of the repository: the entrance, the documentation under `docs/`,
the papers under `.agents/`, the CHANGELOG and the contract.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

LINK = re.compile(r'!?\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)')
FENCE = re.compile(r'^```')
HEADING = re.compile(r'^(#{1,6})\s+(.*?)\s*$')

SKIP = {'.git', 'bin', 'obj', 'artifacts', 'node_modules'}


def anchors_of(path):
    """The anchors of a markdown file, the way GitHub builds them."""
    out = set()
    try:
        with open(path, encoding='utf-8') as handle:
            inside = False
            for line in handle:
                if FENCE.match(line):
                    inside = not inside
                    continue
                if inside:
                    continue
                match = HEADING.match(line)
                if match:
                    title = match.group(2).strip()
                    anchor = re.sub(r'[^\w\s\-]', '', title.lower(), flags=re.UNICODE)
                    out.add(anchor.replace(' ', '-'))
    except OSError:
        pass
    return out


def markdown_files():
    for directory, subdirectories, files in os.walk(ROOT):
        subdirectories[:] = [name for name in subdirectories if name not in SKIP]
        for name in files:
            if name.endswith('.md'):
                yield os.path.relpath(os.path.join(directory, name), ROOT)


def check(relative):
    absolute = os.path.join(ROOT, relative)
    base = os.path.dirname(absolute)
    problems = []
    checked = 0
    with open(absolute, encoding='utf-8') as handle:
        lines = handle.readlines()

    inside = False
    for number, line in enumerate(lines, 1):
        if FENCE.match(line):
            inside = not inside
            continue
        if inside:
            continue
        for target in LINK.findall(line):
            if target.startswith(('http://', 'https://', 'mailto:', '#')):
                if target.startswith('#') and target[1:] not in anchors_of(absolute):
                    problems.append(f'{relative}:{number}: the anchor {target} does not exist')
                continue
            path, _, anchor = target.partition('#')
            if not path:
                continue
            checked += 1
            resolved = os.path.normpath(os.path.join(base, path))
            if not os.path.exists(resolved):
                problems.append(f'{relative}:{number}: {target} does not exist')
            elif anchor and anchor not in anchors_of(resolved):
                problems.append(f'{relative}:{number}: the anchor #{anchor} of {path} does not exist')

    return checked, problems


def main():
    total = 0
    problems = []
    for relative in sorted(markdown_files()):
        checked, found = check(relative)
        total += checked
        problems.extend(found)
        if checked:
            print(f'{relative}: {checked} relative links checked, {len(found)} problems')

    print(f'\nTOTAL checked={total} problems={len(problems)}')
    for problem in problems:
        print(problem)
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
