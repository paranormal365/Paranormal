#!/usr/bin/env bash
# Exports the seeded accounts' passwords as BEN_*_PASSWORD.
#
# WHY THIS IS ITS OWN FILE
#
# `scripts/run-e2e.sh` grew this first, and it is the reason the audio editor's only browser test
# had never once run under the harness: without BEN_*_PASSWORD every sign-in test quietly
# Assert.Ignore's, and an ignored test reports as a pass. The same passwords are needed by runs
# that are NOT the suite — the persona document captures, the iOS documentation capture — and
# those were each deriving them by hand, which is how a value ends up echoed into a log.
#
# Source it; never execute it for its output:
#
#     source scripts/seeded-passwords.sh
#
# The values live in Ben.Data.WebApi/appsettings.Development.json, which is gitignored because the
# repository is public and its dev connection is a real database. Nothing here prints a value.
# Anything already exported in the calling shell wins.

# Sourced from bash AND from zsh (Ben's interactive shell), so the path to this file is found
# without BASH_SOURCE, which zsh does not set.
_BEN_THIS="${BASH_SOURCE[0]:-${(%):-%x}}"
_BEN_SECRETS="$(cd "$(dirname "$_BEN_THIS")/.." && pwd)/Ben.Data.WebApi/appsettings.Development.json"

_ben_seeded_password() {
  # $1 = one of: superadmin | devdata | <email>
  python3 - "$_BEN_SECRETS" "$1" <<'PYEOF' 2>/dev/null || true
import json, sys
d = json.load(open(sys.argv[1])).get("SeedData", {})
who = sys.argv[2]
if who == "superadmin":
    print(d.get("SuperAdmin", {}).get("Password", ""))
elif who == "devdata":
    print(d.get("DevData", {}).get("Password", ""))
else:
    for u in d.get("SeedOrganization", {}).get("Users", []):
        if u.get("Email", "").lower() == who.lower():
            print(u.get("Password", "")); break
PYEOF
}

export BEN_SUPERADMIN_PASSWORD="${BEN_SUPERADMIN_PASSWORD:-$(_ben_seeded_password superadmin)}"
export BEN_USER_PASSWORD="${BEN_USER_PASSWORD:-$(_ben_seeded_password sarah.mitchell@benco.dev)}"
export BEN_MEMBER_PASSWORD="${BEN_MEMBER_PASSWORD:-$(_ben_seeded_password james.thornton@benco.dev)}"
export BEN_CLIENT_PASSWORD="${BEN_CLIENT_PASSWORD:-$(_ben_seeded_password daniel.park@benco.dev)}"
export BEN_VIEWER_PASSWORD="${BEN_VIEWER_PASSWORD:-$(_ben_seeded_password devdata)}"

# `${!name}` is bash-only, so the check reads the value through eval instead — it must not break
# when this file is sourced from zsh, and a silent break here means silently skipped tests.
for _v in BEN_SUPERADMIN_PASSWORD BEN_USER_PASSWORD BEN_MEMBER_PASSWORD BEN_CLIENT_PASSWORD BEN_VIEWER_PASSWORD; do
  eval "_ben_val=\$$_v"
  if [ -z "$_ben_val" ]; then
    echo "   $_v could not be derived from $_BEN_SECRETS — anything signing in with it will be skipped."
  fi
done
unset _v _ben_val _BEN_THIS
