"""Builds the UAT appsettings for the WebApi.

This is a UAT server: a persistent environment Ben develops against, not a public production site.
So it wants the settings he actually runs with, and the previous minimal-production config was the
wrong shape — it stripped the Telerik licence, the Geocodio key, the Entra registration and
AppBaseUrl, none of which are secrets to be withheld from a machine he owns, and all of which turn
features off when absent.

Machine-specific values are overridden rather than carried: the database host, the upload root and
the base URL all differ between his laptop and the server.
"""
import json, pathlib, re, sys

dev_path, out_path, conn, file_root, base_url = sys.argv[1:6]

raw = pathlib.Path(dev_path).read_text()
# The dev file carries // comments, which json refuses.
cfg = json.loads(re.sub(r'^\s*//.*$', '', raw, flags=re.M))


def get(path):
    node = cfg
    for part in path.split(":"):
        if not isinstance(node, dict) or part not in node:
            return None
        node = node[part]
    return node


out = {
    # Machine-specific — always overridden, never carried from the dev file.
    "ConnectionStrings": {"BenDbConnectionString": conn},
    "FileStorage": {"RootPath": file_root},
    "AppBaseUrl": base_url,

    # Serilog's sink holds its own copy of the connection string, so pointing the app at a
    # database is not enough on its own.
    "Serilog": {
        "MinimumLevel": {
            "Default": "Information",
            "Override": {
                "Microsoft.AspNetCore": "Warning",
                "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
            },
        },
        "WriteTo": [{"Name": "MSSqlServer", "Args": {
            "connectionString": conn,
            "tableName": "Logs",
            "autoCreateSqlTable": True,
            "restrictedToMinimumLevel": "Error",
        }}],
        "Properties": {"Application": "Ben.Data.WebApi", "Source": "WebApi"},
    },
}

# Carried across because UAT is a working environment, not a hardened one. Each of these is a
# feature that silently does nothing when the key is absent, which is a worse failure than a loud
# one: geocoding returns nothing, email links point nowhere, Entra sign-in disappears from the
# UI without explanation.
carry = [
    "TelerikKey",              # licence; its absence prints a build/runtime warning
    "Geocodio:ApiKey",         # address lookup — every map feature depends on it
    "Geocodio:BaseUrl",
    "AzureAd:TenantId",        # the real registration; the base file holds only a placeholder,
    "AzureAd:ClientId",        # and Program.cs disables Entra unless ClientId parses as a GUID
    "AzureAd:Audience",
    "SeedData:SuperAdmin:Email",        # so there is an administrator to sign in as
    "SeedData:SuperAdmin:DisplayName",
    "SeedData:SuperAdmin:Password",
    "RateLimits:AuthPerMinute",         # dev relaxes this; a UAT tester retrying a login is not an attack
]

for path in carry:
    value = get(path)
    if value is None:
        continue
    parts = path.split(":")
    node = out
    for part in parts[:-1]:
        node = node.setdefault(part, {})
    node[parts[-1]] = value

pathlib.Path(out_path).write_text(json.dumps(out, indent=2) + "\n")

# Report shape only — never values.
def present(path):
    return "yes" if get(path) is not None else "MISSING from the dev file"

print(f"  TelerikKey            {present('TelerikKey')}")
print(f"  Geocodio:ApiKey       {present('Geocodio:ApiKey')}")
print(f"  AzureAd:ClientId      {present('AzureAd:ClientId')}")
print(f"  SuperAdmin seed       {present('SeedData:SuperAdmin:Email')}")
print(f"  AppBaseUrl            {base_url}")
