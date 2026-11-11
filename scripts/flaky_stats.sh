#!/usr/bin/env bash
# Finds flaky tests: aggregates several runs of the headless suites, per suite
# (its RESULT line) and per check ("NAME: PASS|FAIL — detail" lines).
#
# Usage:
#   ./scripts/flaky_stats.sh --runs=5 [filter]   run run_all_tests.sh N times, then report
#   ./scripts/flaky_stats.sh DIR [DIR ...]       report on saved log dirs (one dir per run,
#                                                each holding <Suite>.log as run_all_tests.sh writes)
#
# Emits observations, not a verdict: runs, passes, fails, fail %, and the pass/fail
# pattern in run order (P = pass, F = fail, - = suite missing/no line in that run).
# A suite with no RESULT line counts as a fail (CLAUDE.md: never judge by absence of FAIL).
# Deciding whether something is flaky or broken is the /test-flakiness skill's job.
#
# Adapted from Claude Code Game Studios' flaky-stats.sh (MIT, see .claude/THIRD_PARTY.md).
set -uo pipefail
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNS=0
FILTER=""
DIRS=()
for arg in "$@"; do
  case "${arg}" in
    --runs=*) RUNS="${arg#--runs=}" ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) if [[ -d "${arg}" ]]; then DIRS+=("${arg}"); else FILTER="${arg}"; fi ;;
  esac
done

if (( RUNS > 0 )); then
  BASE="${FLAKY_DIR:-${TMPDIR:-/tmp}/pte-flaky-$(date +%Y%m%d-%H%M%S)}"
  for ((r = 1; r <= RUNS; r++)); do
    dir="${BASE}/run$(printf '%02d' "${r}")"
    mkdir -p "${dir}"
    echo "== run ${r}/${RUNS} -> ${dir}" >&2
    # Separate process per run: run_all_tests.sh kills headless Godot between suites.
    TEST_LOG_DIR="${dir}" "${PROJECT_DIR}/scripts/run_all_tests.sh" ${FILTER:+"${FILTER}"} > "${dir}/summary.txt" 2>&1
    tail -1 "${dir}/summary.txt" >&2
    DIRS+=("${dir}")
  done
  echo "logs: ${BASE}" >&2
fi

if (( ${#DIRS[@]} == 0 )); then
  echo "no runs: pass --runs=N or log directories" >&2
  exit 2
fi

python3 -I - "${DIRS[@]}" <<'PY'
import os, re, sys
dirs = sys.argv[1:]
check_re = re.compile(r'(?:\[[A-Z]+\] )?([A-Z0-9][A-Z0-9_.-]{2,}|[A-Za-z][\w .-]{2,40}?): (PASS|FAIL)\b')
suites, checks = {}, {}
for i, d in enumerate(dirs):
    for f in sorted(os.listdir(d)):
        if not f.endswith('.log'):
            continue
        name = f[:-4]
        text = open(os.path.join(d, f), errors='replace').read()
        res = re.findall(r'RESULT: (PASS|FAIL)', text)
        suites.setdefault(name, {})[i] = 'P' if res and res[-1] == 'PASS' else 'F'
        seen = {}
        for m in check_re.finditer(text):
            if 'RESULT' in m.group(1):
                continue
            key = f"{name} :: {m.group(1).strip()}"
            # a check printed several times in one run fails the run if any print failed
            seen[key] = 'F' if m.group(2) == 'FAIL' or seen.get(key) == 'F' else 'P'
        for k, v in seen.items():
            checks.setdefault(k, {})[i] = v

def rows(table, only_mixed):
    out = []
    for k, per in table.items():
        pat = ''.join(per.get(i, '-') for i in range(len(dirs)))
        p, f = pat.count('P'), pat.count('F')
        if only_mixed and f == 0:
            continue
        kind = 'always-fail' if p == 0 else ('intermittent' if f else 'stable')
        out.append((-(f / max(p + f, 1)), k, p + f, p, f, pat, kind))
    return sorted(out)

print(f"runs: {len(dirs)}")
print("\n## Suites (by RESULT line)")
print(f"{'suite':40} runs pass fail  fail%  pattern  kind")
for _, k, n, p, f, pat, kind in rows(suites, False):
    print(f"{k:40} {n:4} {p:4} {f:4} {100*f/max(n,1):5.0f}%  {pat:8} {kind}")
print("\n## Checks with at least one FAIL")
mixed = rows(checks, True)
if not mixed:
    print("(none)")
for _, k, n, p, f, pat, kind in mixed:
    print(f"{k:60} {p}/{n} pass  {pat}  {kind}")
stable = sum(1 for r in rows(checks, False) if r[6] == 'stable')
print(f"\n{stable} checks passed in every run they appeared in.")
PY
