# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet — the first release is the assembly of the delivery plan in
[`docs/plan/2026-11-02-lui-repo-plan.md`](./docs/plan/2026-11-02-lui-repo-plan.md).

## [0.1.0] — not published

The first release. It ships the package `Nordmann.LUI` with the API of the
specification ([`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md)):

- `SingleWord` and `MultiWord` indices, `Owned` and `Linked` content sources
- FTS5 trigram search with priority-first candidates, word-start ranking and
  similarity scoring
- typo tolerance over a prefix dictionary
- `Sync`, `Check`, `Rebuild`, `Optimize`
- dependency injection (`AddLui`, keyed services)
- a sample application that exposes the whole API in the browser

[Unreleased]: https://github.com/nordmanndev/LUI/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/nordmanndev/LUI/releases/tag/v0.1.0
