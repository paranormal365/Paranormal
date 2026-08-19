"""Merges the UAT settings into the WebApi's published appsettings.json.

This is a UAT server: a persistent environment Ben develops against, not a public production site.
So it wants the settings he actually runs with, and a minimal production config was the wrong
shape — it stripped the Telerik licence, the Geocodio key, the Entra registration and AppBaseUrl,
none of which are secrets to be withheld from a machine he owns, and all of which turn features
off silently when absent.

Written into appsettings.json rather than appsettings.Production.json, deliberately. An
environment-specific file only loads when ASPNETCORE_ENVIRONMENT matches, and a copy-deployed
package has no say in what that variable says on the far end. The first attempt put the upload
root in Production.json; the server started with an environment that did not load it, fell back
to the empty string in the base file, and refused to start with "FileStorage:RootPath is not
configured" — a setting that was present in the package and simply never read. The base file
always loads, whatever the environment.

Everything already in appsettings.json is preserved: the merge is by key, so Smtp, rate limits and
logging survive untouched.
"""
import json, pathlib, re, sys

dev_path, app_settings_path, conn, file_root, base_url = sys.argv[1:6]

raw = pathlib.Path(dev_path).read_text()
# The dev file carries // comments, which json refuses.
dev = json.loads(re.sub(r'^\s*//.*$', '', raw, flags=re.M))
base = json.loads(pathlib.Path(app_settings_path).read_text())


def get(cfg, path):
    node = cfg
    for part in path.split(":"):
        if not isinstance(node, dict) or part not in node:
            return None
        node = node[part]
    return node


def put(cfg, path, value):
    parts = path.split(":")
    node = cfg
    for part in parts[:-1]:
        node = node.setdefault(part, {})
    node[parts[-1]] = value


# Machine-specific — always ours, never carried from the dev file.
put(base, "ConnectionStrings:BenDbConnectionString", conn)
put(base, "FileStorage:RootPath", file_root)
put(base, "AppBaseUrl", base_url)

# Serilog's sink holds its own copy of the connection string, so pointing the app at a database is
# not enough on its own.
put(base, "Serilog:WriteTo", [{"Name": "MSSqlServer", "Args": {
    "connectionString": conn,
    "tableName": "Logs",
    "autoCreateSqlTable": True,
    "restrictedToMinimumLevel": "Error",
}}])

# Carried across because UAT is a working environment, not a hardened one. Each of these is a
# feature that silently does nothing when its key is absent, which is worse than failing loudly:
# geocoding returns nothing, email links point nowhere, and Entra sign-in disappears from the UI
# because Program.cs disables it unless ClientId parses as a GUID.
carry = [
    "TelerikKey",
    "Geocodio:ApiKey",
    "Geocodio:BaseUrl",
    "AzureAd:TenantId",
    "AzureAd:ClientId",
    "AzureAd:Audience",
    "SeedData:SuperAdmin:Email",         # so there is an administrator to sign in as
    "SeedData:SuperAdmin:DisplayName",
    "SeedData:SuperAdmin:Password",
    "RateLimits:AuthPerMinute",          # a UAT tester retrying a login is not an attack
]

missing = []
for path in carry:
    value = get(dev, path)
    if value is None:
        missing.append(path)
        continue
    put(base, path, value)

pathlib.Path(app_settings_path).write_text(json.dumps(base, indent=2) + "\n")

# Report shape only — never values.
print(f"  merged into appsettings.json (loads in every environment)")
for path in carry:
    if path in missing:
        print(f"    {path:34} MISSING from the dev file")
print(f"    {'FileStorage:RootPath':34} {file_root}")
print(f"    {'AppBaseUrl':34} {base_url}")
