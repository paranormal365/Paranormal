#!/usr/bin/env bash
set -euo pipefail

# Where a run has got to: what has passed, what failed, and what has not been reached yet.
#
# WHY THIS EXISTS
#
# run-e2e.sh runs the suite at minimal verbosity on purpose — a passing test prints nothing, so the
# terminal stays readable and a failure is impossible to miss. The cost is that while a thirty-odd
# minute run is in flight there is no way to answer "where are we?", and a quiet log looks identical
# whether the suite is a tenth of the way through or nearly done. Asked for 2026-09-19.
#
# So run-e2e.sh now writes the full per-test stream to its log and the list of tests it INTENDS to
# run beside it, and this reads the two against each other. It is throwaway: everything it reads
# lives in the run's own temp directory and goes when the machine is next cleaned out.
#
# USAGE
#   scripts/e2e-progress.sh              # the run that is going now, or the last one
#   scripts/e2e-progress.sh --remaining  # also list what has not been reached
#   scripts/e2e-progress.sh /path/to/logdir
#
# It reads and prints. It never touches the run.

POINTER="${TMPDIR:-/tmp}/ben-e2e-current"
SHOW_REMAINING=0
LOG_DIR=""

for arg in "$@"; do
  case "$arg" in
    --remaining) SHOW_REMAINING=1 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *)  LOG_DIR="$arg" ;;
  esac
done

if [[ -z "$LOG_DIR" ]]; then
  if [[ ! -f "$POINTER" ]]; then
    echo "No run to report on: $POINTER is not there."
    echo "It is written when scripts/run-e2e.sh starts, so either nothing has run on this machine"
    echo "since it was last cleaned out, or the run predates this script."
    exit 1
  fi
  LOG_DIR="$(cat "$POINTER")"
fi

STREAM="$LOG_DIR/e2e.log"
PLANNED="$LOG_DIR/planned.txt"
META="$LOG_DIR/meta"

[[ -d "$LOG_DIR" ]] || { echo "That run's directory is gone: $LOG_DIR"; exit 1; }

started="" ; db="" ; filter=""
[[ -f "$META" ]] && { . "$META" 2>/dev/null || true; }
started="${E2E_STARTED:-}" ; db="${E2E_DB:-unknown}" ; filter="${E2E_FILTER:-}"

# ── Is it still going? ────────────────────────────────────────────────────────
if pgrep -f "run-e2e.sh" >/dev/null 2>&1; then
  state="running"
elif grep -q '── Result ──' "$STREAM" 2>/dev/null || [[ -f "$LOG_DIR/.finished" ]]; then
  state="finished"
else
  state="stopped early"
fi

elapsed="?"
if [[ -n "$started" ]]; then
  now=$(date +%s)
  secs=$(( now - started ))
  elapsed="$(( secs / 60 ))m$(printf '%02d' $(( secs % 60 )))s"
fi

# ── What the stream says so far ───────────────────────────────────────────────
# `dotnet test` at normal verbosity prints one line per test as it completes. Skipped tests are
# counted apart: the suite deliberately skips its capture and walk fixtures on an ordinary run, and
# reading those as progress is a mistake that has been made here before.
count() { grep -cE "$1" "$STREAM" 2>/dev/null | tr -d ' ' || echo 0; }
passed=$(count '^ *Passed ')
failed=$(count '^ *Failed ')
skipped=$(count '^ *Skipped ')
done_now=$(( passed + failed + skipped ))

total="?"
if [[ -f "$PLANNED" ]]; then
  total=$(grep -cve '^[[:space:]]*$' "$PLANNED" 2>/dev/null | tr -d ' ')
fi

echo "e2e · ${db}${filter:+ · filter: $filter} · ${state} · ${elapsed}"
if [[ "$total" != "?" && "$total" -gt 0 ]]; then
  pct=$(( done_now * 100 / total ))
  echo "  ${done_now} of ${total} reached (${pct}%) · passed ${passed} · failed ${failed} · skipped ${skipped}"
else
  echo "  ${done_now} reached · passed ${passed} · failed ${failed} · skipped ${skipped}"
  echo "  (no planned list for this run, so there is no \"how many left\" to give)"
fi

last=$(grep -E '^ *(Passed|Failed|Skipped) ' "$STREAM" 2>/dev/null | tail -1 | sed 's/^ *//; s/ \[.*//')
[[ -n "$last" ]] && echo "  last finished: $last"

if [[ "$failed" -gt 0 ]]; then
  echo ""
  echo "  failed so far:"
  grep -E '^ *Failed ' "$STREAM" | sed 's/^ *Failed /    - /; s/ \[.*//' | head -25
fi

# ── What has not been reached ─────────────────────────────────────────────────
if [[ -f "$PLANNED" && "$total" != "?" ]]; then
  finished_names="$LOG_DIR/.finished-names"
  grep -E '^ *(Passed|Failed|Skipped) ' "$STREAM" 2>/dev/null \
    | sed -E 's/^ *(Passed|Failed|Skipped) //; s/ \[.*//' | sort -u > "$finished_names" || true

  # --list-tests gives fully-qualified names; the console logger gives the short name. Compare on
  # the last dotted segment so the two line up.
  remaining=$(
    awk '{gsub(/^[[:space:]]+|[[:space:]]+$/,""); if ($0 != "") { n=split($0,p,"."); print p[n] }}' "$PLANNED" \
      | sort -u | comm -23 - "$finished_names" 2>/dev/null || true
  )
  n_remaining=$(printf '%s\n' "$remaining" | grep -cve '^[[:space:]]*$' | tr -d ' ')

  if [[ "$n_remaining" -gt 0 ]]; then
    echo ""
    echo "  not reached yet: ${n_remaining}"
    if [[ "$SHOW_REMAINING" -eq 1 ]]; then
      printf '%s\n' "$remaining" | sed 's/^/    · /'
    else
      printf '%s\n' "$remaining" | head -8 | sed 's/^/    · /'
      [[ "$n_remaining" -gt 8 ]] && echo "    … $(( n_remaining - 8 )) more (--remaining for all)"
    fi
  fi
fi

echo ""
echo "  logs: $LOG_DIR"
