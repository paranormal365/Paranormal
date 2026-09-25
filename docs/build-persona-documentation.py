#!/usr/bin/env python3
"""Builds one developer document per website user type.

Seven documents, one per seat, each named for its user type:

    IsHaunted-Web-Visitor.pdf         nobody signed in
    IsHaunted-Web-Client.pdf          someone who asked a group for help
    IsHaunted-Web-Member.pdf          an ordinary member of a group
    IsHaunted-Web-Viewer.pdf          a member who may look and change nothing
    IsHaunted-Web-Owner.pdf           a group's owner or administrator
    IsHaunted-Web-SuperAdmin.pdf      runs the site
    IsHaunted-Web-Seller.pdf          a member who sells their own gear through the store

**One document per seat rather than one document with six chapters**, because the site is a
different application from each of them and a reader only needs their own. The permission model is
not decoration here: an administrator passes every check by role, so a surface that is broken for
everybody else looks perfect from that seat — which is exactly why each document is captured while
signed in as that person rather than described from the code.

    python3 docs/build-persona-documentation.py            # all seven
    python3 docs/build-persona-documentation.py member     # just one

Every screenshot is of simulated, seeded data, captured in dark mode at 1440x900.
"""
import glob
import os
import subprocess
import sys

DOCS = os.path.dirname(os.path.abspath(__file__))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

CSS = """
@page { size: A4; margin: 15mm 13mm; }
body { font: 11pt/1.55 -apple-system, "Helvetica Neue", Arial, sans-serif; color: #16181c; margin: 0; }
h1 { font-size: 25pt; margin: 0 0 2mm; letter-spacing: -0.4px; }
h2 { font-size: 14.5pt; margin: 8mm 0 2mm; padding-top: 3mm; border-top: 1px solid #d8dce2;
     page-break-after: avoid; }
.sub { color: #5b626d; font-size: 11pt; margin: 0 0 5mm; }
p { margin: 0 0 3mm; }
code, .mono { font-family: ui-monospace, Menlo, monospace; font-size: 9.5pt;
              background: #f1f3f6; padding: 0 3px; border-radius: 3px; }
.rule { border-left: 3px solid #2e6b34; padding-left: 4mm; background: #f4f8f4;
        padding-top: 2mm; padding-bottom: 2mm; }
.cover { border: 1px solid #d8dce2; border-radius: 6px; padding: 5mm; background: #fafbfc;
         margin-bottom: 6mm; }
figure { margin: 4mm 0; page-break-inside: avoid; text-align: center; }
figure img { max-width: 100%; max-height: 130mm; border: 1px solid #d0d5dc; border-radius: 6px; }
figcaption { color: #5b626d; font-size: 9pt; margin-top: 1.5mm; }
table { border-collapse: collapse; width: 100%; margin: 3mm 0; font-size: 9.5pt; }
th, td { border: 1px solid #d8dce2; padding: 2mm 2.5mm; text-align: left; vertical-align: top; }
th { background: #f1f3f6; }
"""

# The rules every document repeats, because a reader of one will not read the others.
SHARED = """
<h2>How this site decides what you may see</h2>

<p>Two things gate every surface, and they are different questions:</p>

<p><b>Your seat in a group</b> — owner, administrator, member, viewer — answers "may this person do
this?". <b>The group's plan</b> answers "does this group pay for this at all?". A member of a group
whose plan excludes an area is refused for a completely different reason than a viewer who is not
allowed to write, and the site says which.</p>

<p class="rule"><b>A refusal is never rendered as "nothing here."</b> An empty list and a server
saying no are different facts, and a page that shows the same grey nothing for both teaches people
to distrust it. Every list goes through <code>LoadResult</code>, which carries loading, empty,
refused, session-ended and rate-limited as distinct states — and every one of them has its own
words on screen.</p>

<p>This matters when reading this document: where a screenshot shows a refusal, that <i>is</i> the
designed behaviour for this seat, not a broken page.</p>
"""


def shots(persona):
    d = os.path.join(DOCS, "web-media", persona)
    return sorted(glob.glob(os.path.join(d, "*.png")))


def figure(path, caption):
    return (f"<figure><img src='{os.path.relpath(path, DOCS)}'>"
            f"<figcaption>{caption}</figcaption></figure>")


PERSONAS = {
    "visitor": {
        "title": "The visitor",
        "who": "Somebody with no account, who arrived from a search or a link.",
        "intro": """
<p>The visitor is the largest audience and the one with the least patience. Everything they can
reach is public on purpose: the site's whole growth argument is that somebody researching a
haunted address finds a real archive rather than a sign-up wall.</p>

<p>What they can do: read the feed, browse public places and their archives, find groups near them,
read published investigations, and ask a group for help. What they cannot do is anything tied to an
identity — which is where the refusals below come from.</p>
""",
        "notes": {
            "10-home": "The front door. Search by place, and what is near you.",
            "11-find-groups": "Groups a person can approach, with what each one covers.",
            "12-feed": "The public feed. Reading is open to everyone; posting is not.",
            "13-request-an-investigation": "The request flow — the main conversion path on the site.",
            "14-sign-in": "Sign in, including Microsoft and Apple.",
            "15-sign-up": "Registration. The @name is permanent and the form says so.",
            "16-help": "The in-app help, readable without an account.",
            "17-refused-my-cases": "A page that needs an account. Note it explains rather than "
                                   "showing an empty list — this is the refusal rule in action.",
            "18-a-hosted-event": "A hosted event's public page (item 235): the venue's photographs, the nights, "
                                 "what is left, and how to get in and around.",
            "19-picking-seats-without-an-account": "Seats picked with no account: the visitor is emailed a link "
                                                   "to hold them, and nothing is held until they open it.",
            "1b-the-store": "The gear store's front (storefront). Nobody needs an account to buy. Hidden, menus "
                            "and all, until the store is switched on — photographed switched on.",
            "1c-a-sellers-item": "An item a member makes and sells (store sellers): it ships from them, and its "
                                 "questions and answers sit after the reviews. Asking needs an account.",
        },
    },
    "client": {
        "title": "The client",
        "who": "Somebody who asked a group to investigate their home or business.",
        "intro": """
<p>The client is not a member of any group. They have one relationship — with the group working
their case — and the site is deliberately narrow for them: their case, their messages, their
evidence, and nothing about how the group runs itself.</p>

<p>This is the seat where privacy matters most. A private residence is redacted at display time
rather than being stored differently, so what a client sees and what the public sees diverge on
every render rather than depending on somebody having remembered to set a flag.</p>
""",
        "notes": {
            "20-my-cases": "The client's own cases and their status.",
            "21-my-requests": "Requests they have made, including ones not yet accepted.",
            "22-notifications": "What has happened on their case.",
            "23-my-evidence": "What they captured or were given a copy of.",
            "24-pricing": "What the paid plans cover, if they want their own account.",
            "25-profile": "Their account and contact details.",
            "26-refused-admin": "Administration, refused. The site names the refusal.",
            "27-asking-for-a-room": "As a guest at somebody's hosted event: asking for a room. Nothing is held or "
                                    "paid here.",
            "28-what-im-going-to": "Every hosted event this person has a place at, with the pass and the event's "
                                   "own screen.",
            "29-event-pass": "The QR pass for the door. The one pictured has since been withdrawn.",
        },
    },
    "member": {
        "title": "The ordinary member",
        "who": "Belongs to a group, holds no named role and no extra grants.",
        "intro": """
<p><b>This is the most useful seat to test and the easiest to get wrong.</b> An ordinary member
belongs to the group but holds no grants, so the permission service returns false on every table.
Anything a member is meant to reach that is gated on a grant is broken from here <i>and nowhere
else</i> — an owner or administrator would never see it.</p>

<p>A developer changing permissions should check this seat first. The test suite keeps a permanent
account for exactly this reason.</p>
""",
        "notes": {
            "30-home": "The member's landing page — their desk: next investigation, open cases, unread, gear out.",
            "31-organizations": "The groups they belong to.",
            "32-my-investigations": "Investigations they are part of.",
            "33-media-library": "Shared media for their group.",
            "34-my-equipment": "Equipment assigned to or borrowed by them.",
            "35-events": "Events, including public ones they can attend.",
            "36-feed": "The feed, where a member can post as themselves or as the group.",
            "37-profile": "Account, security and privacy.",
            "38-refused-admin": "Site administration, refused.",
            "39-asked-to-help-at-an-event": "An invitation to help at a hosted event. It grants only what it "
                                            "names — here, the door — whatever the member's seat in the group.",
            "3a-the-door-on-a-phone": "The door for tonight on a phone: who is expected, search by name or pass "
                                      "code, and walk-ups.",
            "3c-my-orders": "Their store orders and where each has got to. An order with items from more than "
                            "one sender comes as that many packages, each with its own tracking.",
            "3d-my-questions": "Questions they asked about store items, and the answers — private to them "
                               "unless copied into an item's FAQ, which never names them.",
        },
    },
    "viewer": {
        "title": "The viewer",
        "who": "Belongs to a group, may look, changes nothing.",
        "intro": """
<p>The viewer exists so that a group can bring somebody in — a trainee, a property owner, an
interested colleague — without giving them the ability to alter the record.</p>

<p>The interesting question for a developer is what a read-only seat should look like. The answer
this site chose: <b>the controls are absent, not disabled-and-mocking</b>. A greyed-out button that
never works is worse than no button, because it invites a click and then refuses it.</p>
""",
        "notes": {
            "40-home": "The viewer's landing page — the same shell, fewer doors.",
            "41-organizations": "Their group, read-only.",
            "42-my-investigations": "Investigations they may read.",
            "43-media-library": "Media they may look at but not change.",
            "44-my-equipment": "Equipment, without the ability to check anything out.",
        },
    },
    "owner": {
        "title": "The group owner and administrator",
        "who": "Runs a group: its people, its cases, its equipment and its bill.",
        "intro": """
<p>The owner is the paying customer, and the seat where the business model becomes visible. They
manage members and their roles, accept or decline investigation requests, run cases and
investigations, hold the equipment inventory, and carry the subscription.</p>

<p class="rule"><b>A caution for developers.</b> An administrator passes every permission check by
role, so <i>every surface looks perfect from this seat</i>. It is the worst possible place to
verify a change to permissions and the easiest place to be fooled. Check the member and viewer
documents for what the same page looks like without those grants.</p>
""",
        "notes": {
            "50-home": "The owner's landing page, including work waiting on them.",
            "51-organizations": "Their groups and the management surfaces.",
            "52-my-cases": "Cases the group is working.",
            "53-my-investigations": "Investigations across the group.",
            "54-my-equipment": "The equipment inventory and its checkouts.",
            "55-events": "Events the group runs, public and private.",
            "56-org-subscriptions": "The subscription: what the plan covers and what it costs.",
            "57-profile": "Their own account.",
            "58-tours": "Tours, for a business that runs walks — the product the tour plan is "
                        "priced on, and the door to each tour's guides, guest email and gallery.",
            "59-event-at-a-glance": "A hosted event's numbers: people coming, parties waiting, places left "
                                    "night by night, arrivals and reviews.",
            "5a-confirming-a-booking": "Confirming a party into the rooms the organizer chooses, with a line "
                                       "the guest reads. The pass goes with the confirmation.",
            "5b-writing-to-guests": "One letter to everybody with a place, with the count shown before it is sent.",
            "5c-venue-profile": "The group's venue: its history, house rules and photo library.",
        },
    },
    "superadmin": {
        "title": "The site administrator",
        "who": "Runs the platform itself. Passes every permission check, everywhere.",
        "intro": """
<p>The SuperAdmin operates the site rather than using it: people, groups, taxonomies, billing,
moderation, and the diagnostics that say whether the platform is healthy.</p>

<p>Two of these screens exist because something went wrong and could not be seen. <b>Outgoing
Mail</b> exists because a real sign-up produced no email and nothing anywhere recorded it — a
failure that is invisible from every other surface, since the account exists and the site is up.
<b>Rate limits</b> and the <b>audit log</b> are the same idea: the platform explaining itself
rather than requiring somebody to read the database.</p>
""",
        "notes": {
            "60-dashboard": "How the site is being used. Counts accounts and their activity — "
                            "visitors who never sign in are not recorded anywhere, and it says so.",
            "61-users": "Every account. The Verified column distinguishes confirmed, "
                        "link-sent-not-used, and never-emailed.",
            "62-admin-cases": "Every case on the platform.",
            "63-site-settings": "Feature flags, announcements, and what the site offers at all.",
            "64-audit-log": "Who changed what, when. Filtered and paged on the server.",
            "65-outgoing-mail": "Whether this machine can actually send email, and a real test send.",
            "66-rate-limits": "What the site's limits have turned away.",
            "67-billing-ledger": "The money trail, append-only.",
            "68-referrals": "Referral standings and what they earn.",
            "69-support-tickets": "The support queue.",
            "6d-store-dashboard": "The store's back office: orders to pack and ship, reviews and sale requests "
                                  "waiting, and what is running low. Works while the store is switched off.",
            "6e-store-orders": "The order desk. Each seller's items travel as their own package, so an order can "
                               "read Partially shipped.",
            "6f-sale-requests": "Sellers asking for items to go on sale. The store sets the price, then approves.",
            "6g-sellers": "What each seller has earned, what is ready to pay, and what has been paid. Payouts are "
                          "made by hand outside the site and recorded here.",
            "6h-store-settings": "The store's settings, and the checklist that must be empty before it opens.",
            "6a-events-dashboard": "Hosted events across every group: where they stand, credits, venues and "
                                   "appeals waiting.",
            "6b-every-event": "Every hosted event, with view and remove.",
            "6c-removing-an-event": "Removal says what it will do before it is done: off the site, guests told, "
                                    "credit returned, the organizer offered an appeal.",
        },
    },
    "seller": {
        "title": "The seller",
        "who": "A member who makes equipment and sells it through the store.",
        "intro": """
<p>The seller is an ordinary member everywhere else; <b>Seller</b> is an additive site role, given on
the person's Site Roles tab. It opens a <b>Selling</b> workspace — in the menu once the store is
switched on — for their own items and nobody else's. Every seller endpoint starts from the caller's
own items, and another seller's item answers 404 rather than 403.</p>

<p>The split of powers is the design: a seller writes their words, pictures, options, stock, parts
list, files, FAQ and versions, and asks for an item to go on sale; the store sets every price and
approves. Each seller ships their own package with their own tracking, and is paid cost plus their
approved asking price per unit, by hand, against an append-only earnings ledger.</p>
""",
        "notes": {
            "70-my-items": "Their items and where each stands: draft, on sale, off sale. The price is shown, "
                           "never editable.",
            "71-an-item": "One item's editor. Every change lands in the item's history, marked as theirs.",
            "72-parts-and-cost": "The parts list: what a unit costs to make. Never shown to shoppers.",
            "73-files": "Manuals and firmware, each for buyers (downloadable from a paid order) or private.",
            "74-my-packages": "Paid orders with their items in: the address, and Ship with carrier and tracking.",
            "75-my-earnings": "Owed, ready to pay, paid — by item, order and year, for tax forms.",
            "76-questions": "Shoppers' questions, with nobody's name on them. Answer, decline, or add to the FAQ.",
            "77-refused-store-admin": "The store's own administration refuses a seller.",
        },
    },
}


def build(key):
    p = PERSONAS[key]
    parts = [f"<!doctype html><html><head><meta charset='utf-8'>"
             f"<title>IsHaunted — {p['title']}</title><style>{CSS}</style></head><body>"]
    parts.append(f"<h1>IsHaunted &mdash; {p['title']}</h1>")
    parts.append(f"<p class='sub'>{p['who']} Written for a developer joining the project.</p>")
    parts.append(
        "<div class='cover'><p style='margin:0'><b>Every screenshot shows simulated, seeded "
        "data</b>, captured in dark mode while signed in as this user type. Nothing here is a real "
        "person, case or investigation. Where a page shows a refusal, that is the designed "
        "behaviour for this seat.</p></div>")
    parts.append(p["intro"])
    parts.append(SHARED)
    parts.append("<h2>What this user sees, screen by screen</h2>")

    found = shots(key)
    if not found:
        parts.append("<p><i>No screenshots captured for this user type.</i></p>")
    for path in found:
        stem = os.path.splitext(os.path.basename(path))[0]
        note = p["notes"].get(stem, "")
        heading = stem.split("-", 1)[1].replace("-", " ").capitalize()
        parts.append(f"<p><b>{heading}.</b> {note}</p>")
        parts.append(figure(path, heading))

    parts.append("</body></html>")

    html = os.path.join(DOCS, f"ishaunted-web-{key}.html")
    with open(html, "w") as f:
        f.write("\n".join(parts))

    pdf = os.path.join(DOCS, f"IsHaunted-Web-{p['title'].split()[-1].capitalize()}.pdf")
    # Named for the user type, so the file itself says who it is for.
    pdf = os.path.join(DOCS, f"IsHaunted-Web-{key.capitalize()}.pdf")
    subprocess.run([CHROME, "--headless", "--no-pdf-header-footer",
                    f"--print-to-pdf={pdf}", f"file://{html}"],
                   capture_output=True, check=False)
    size = os.path.getsize(pdf) if os.path.exists(pdf) else 0
    print(f"  {os.path.basename(pdf):<34} {len(found):>2} screens  {size/1024:>7.0f} KB")


def main():
    wanted = sys.argv[1:] or list(PERSONAS)
    for key in wanted:
        if key not in PERSONAS:
            raise SystemExit(f"unknown user type '{key}' — one of {', '.join(PERSONAS)}")
        build(key)


if __name__ == "__main__":
    main()
