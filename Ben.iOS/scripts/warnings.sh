#!/bin/zsh
# The warnings from the newest Xcode build or archive of this project — run it after an archive.
#
# Why: deprecations arrive a few at a time with each Xcode, and are cheap to fix while they are few
# (Ben, 10/14/2026). This reads the log Xcode already wrote, so it builds nothing and is safe to run
# at any time.
#
#   scripts/warnings.sh          warnings from the newest log
#   scripts/warnings.sh --all    also list each log it could have read, newest first
#
# One line is left out on purpose: "Metadata extraction skipped, no AppIntents.framework dependency
# found". Xcode prints it for every app that does not use App Intents (Shortcuts), and the only way
# to silence it is to link a framework the app does not use.
set -euo pipefail
cd "$(dirname "$0")/.."
project="${WARNINGS_PROJECT:-$PWD/IsHaunted.xcodeproj}"

# Each DerivedData folder says which project it belongs to; only this checkout's count.
logs=()
for dir in ~/Library/Developer/Xcode/DerivedData/IsHaunted-*(N/); do
  [[ "$(plutil -extract WorkspacePath raw "$dir/info.plist" 2>/dev/null)" == "$project" ]] || continue
  logs+=("$dir"/Logs/Build/*.xcactivitylog(N))
done
if (( ${#logs} == 0 )); then
  echo "No Xcode build log for $project yet. Build or archive in Xcode first." >&2
  exit 1
fi

newest=(${(f)"$(ls -t $logs)"})
if [[ "${1:-}" == "--all" ]]; then
  for log in $newest; do echo "$(stat -f '%Sm' -t '%m/%d/%Y %H:%M' "$log")  $log"; done
  echo
fi

log=$newest[1]
echo "From the build of $(stat -f '%Sm' -t '%m/%d/%Y %I:%M %p' "$log")"
echo

# The log is gzipped, with carriage returns between records. Each diagnostic is
# "path:line:col: warning: message", and the source excerpt that follows runs on without a
# separator, so the message is cut where the excerpt's "NN |" starts. Xcode records each one more
# than once (per architecture, and again in the summary); sort -u keeps one.
found=$(gunzip -c "$log" \
  | LC_ALL=C tr '\r' '\n' \
  | LC_ALL=C grep -ao '/[A-Za-z][^ "*]*:[0-9]*:[0-9]*: warning: .*' \
  | LC_ALL=C sed -E 's/(\[#[A-Za-z]+\]).*/\1/; s/[0-9]+ \|.*$//; s/[[:space:]]+$//' \
  | sed "s#${project:h:h}/##" \
  | sort -u || true)

if [[ -z "$found" ]]; then
  echo "No warnings."
  exit 0
fi

echo "$found"
echo
echo "$(echo "$found" | wc -l | tr -d ' ') warnings."
