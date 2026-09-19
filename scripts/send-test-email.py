#!/usr/bin/env python3
"""Sends one test email through the configured SMTP server.

Reads the same settings the API uses (Ben.Data.WebApi/appsettings.json, "Smtp" section) so a
successful send here means the API's own configuration is right — host, port, TLS mode, username
and from-address are not retyped anywhere.

The password is the one thing it does not read from configuration. It is taken from the
Smtp__Password environment variable if that is set, and otherwise typed at the prompt, where it is
not echoed and does not reach shell history:

    python3 scripts/send-test-email.py you@example.com

Exit codes: 0 sent, 1 configuration problem, 2 the server refused.
"""
import json
import os
import re
import smtplib
import ssl
import sys
from email.message import EmailMessage
from getpass import getpass
from pathlib import Path

CONFIG = Path(__file__).resolve().parent.parent / "Ben.Data.WebApi" / "appsettings.json"


def load_smtp_settings() -> dict:
    raw = CONFIG.read_text()
    # The file carries // comments in places; strip them outside of strings well enough to parse.
    raw = re.sub(r'^\s*//.*$', '', raw, flags=re.M)
    smtp = json.loads(raw).get("Smtp")
    if not smtp:
        sys.exit(f"No \"Smtp\" section in {CONFIG}.")
    if not smtp.get("Host"):
        sys.exit("Smtp:Host is not set, so the app considers email disabled.")
    return smtp


def main() -> int:
    if len(sys.argv) != 2:
        sys.exit("usage: send-test-email.py <recipient>")

    recipient = sys.argv[1]
    smtp = load_smtp_settings()

    host = smtp["Host"]
    port = int(smtp.get("Port", 587))
    user = smtp.get("User") or ""
    sender = smtp.get("FromAddress") or "no-reply@example.com"
    from_name = smtp.get("FromName") or "IsHaunted.com"
    # SslOnConnect means TLS from the first byte (465). StartTls upgrades a plain connection (587).
    implicit_tls = str(smtp.get("Security", "StartTls")).lower() == "sslonconnect"

    password = os.environ.get("Smtp__Password") or getpass(f"Password for {user}: ")
    if not password:
        sys.exit("No password given.")

    message = EmailMessage()
    message["From"] = f"{from_name} <{sender}>"
    message["To"] = recipient
    message["Subject"] = "IsHaunted.com — SMTP test"
    message.set_content(
        "This is a test message from IsHaunted.com.\n\n"
        f"Sent through {host}:{port} as {user}.\n"
        "If it arrived, outgoing mail is configured correctly and the site can send "
        "invitations, confirmations and notifications.\n"
    )

    print(f"connecting to {host}:{port} ({'implicit TLS' if implicit_tls else 'STARTTLS'})…")
    context = ssl.create_default_context()

    try:
        if implicit_tls:
            server = smtplib.SMTP_SSL(host, port, context=context, timeout=30)
        else:
            server = smtplib.SMTP(host, port, timeout=30)
        with server:
            server.ehlo()
            if not implicit_tls:
                server.starttls(context=context)
                server.ehlo()
            if user:
                server.login(user, password)
            server.send_message(message)
    except smtplib.SMTPAuthenticationError as ex:
        print(f"the server rejected the credentials: {ex}", file=sys.stderr)
        return 2
    except Exception as ex:                      # noqa: BLE001 — report whatever the server said
        print(f"send failed: {type(ex).__name__}: {ex}", file=sys.stderr)
        return 2

    print(f"sent to {recipient}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
