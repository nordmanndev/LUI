#!/usr/bin/env bash
#
# make-curated-corpus.sh — regenerate the checked-in word sample that the
# normalisation parity test runs on every pull request.
#
# The sample is a deterministic stride over the full list: every N-th word.
# A stride is used instead of a hand-picked subset because a hand-picked
# subset drifts towards the letters the author already thought of, and the
# parity test is exactly the place where the *unthought-of* letters fail.
#
# Usage:
#   tools/fetch-wordlist.sh          # the full list must be present
#   tools/make-curated-corpus.sh     # regenerates the sample
#
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly FULL="${ROOT}/artifacts/wordlist/german-words.txt"
readonly TARGET_DIR="${ROOT}/tests/TestData/wordlist"
readonly TARGET="${TARGET_DIR}/german-words.sample.txt"
readonly STRIDE=97

if [ ! -f "${FULL}" ]; then
  echo "make-curated-corpus: ${FULL} is missing — run tools/fetch-wordlist.sh first" >&2
  exit 1
fi

mkdir -p "${TARGET_DIR}"

awk -v stride="${STRIDE}" 'NR % stride == 1' "${FULL}" > "${TARGET}"

words="$(wc -l < "${TARGET}" | tr -d ' ')"
echo "make-curated-corpus: ${words} words (every ${STRIDE}th) → ${TARGET}"
