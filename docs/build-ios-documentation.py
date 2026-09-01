#!/usr/bin/env python3
"""Builds the iPhone and iPad developer documents from captured simulator screenshots.

Two documents, one per device, because they are for two different people: the shell differs
(a TabView on iPhone, a NavigationSplitView on iPad) and so does everything that follows from it.
A single document covering both would make each reader skip half of it.

    python3 docs/build-ios-documentation.py iphone
    python3 docs/build-ios-documentation.py ipad

Then print it, exactly as the product documentation is printed:

    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless \
      --no-pdf-header-footer --print-to-pdf=docs/IsHaunted-iOS-iPhone.pdf \
      "file://$PWD/docs/ishaunted-ios-iphone.html"

**Every screenshot is of simulated data.** The accounts, cases, feed posts and recordings are
seeded; the photographs and waveforms in the feed are generated. Nothing here is a real
investigation, and the documents say so on the first page so a reader never has to wonder.

Screenshots are read from `docs/ios-media/<device>/` and matched by their numeric prefix, so a
capture that fails to reach a screen leaves that section without a picture rather than breaking
the build — the text still describes it.
"""
import glob
import os
import re
import sys

DEVICE = (sys.argv[1] if len(sys.argv) > 1 else "iphone").lower()
if DEVICE not in ("iphone", "ipad"):
    raise SystemExit("usage: build-ios-documentation.py [iphone|ipad]")

DOCS = os.path.dirname(os.path.abspath(__file__))
MEDIA = os.path.join(DOCS, "ios-media", DEVICE)
DEVICE_NAME = "iPhone" if DEVICE == "iphone" else "iPad"
SHELL = ("a <code>TabView</code> across the bottom" if DEVICE == "iphone"
         else "a <code>NavigationSplitView</code> sidebar down the left")
OTHER = "iPad" if DEVICE == "iphone" else "iPhone"


def shot(prefix):
    """The captured screenshot whose name starts with this prefix, or None."""
    hits = sorted(glob.glob(os.path.join(MEDIA, prefix + "*.png")))
    return os.path.relpath(hits[0], DOCS) if hits else None


# ── The document ──────────────────────────────────────────────────────────────
# Each section is (heading, screenshot prefix or None, html body).

SECTIONS = [
    ("What this is", None, f"""
<p>IsHaunted is a platform for paranormal investigation groups and the people who ask them for
help. The website runs the business — cases, clients, groups, billing. <b>This app is the part
that goes into the building at 2am.</b></p>

<p>It is a native Swift 6 / SwiftUI universal app. One target runs on both devices and picks its
shell by size class: {SHELL} on {DEVICE_NAME}. This document covers the {DEVICE_NAME}; the {OTHER}
has its own.</p>

<p class="rule"><b>It is a pure REST client.</b> The app talks to <code>Ben.Data.WebApi</code> and
nothing else. <code>Ben.iOS/</code> is deliberately not referenced by <code>Ben.slnx</code>, so
the website and the API never see it and cannot be broken by it. Roughly 90% of the code lives in
<code>BenKit</code>, a local Swift package — networking, auth, models, config — which is where to
start reading.</p>
"""),

    ("Four rules the code follows", None, """
<p>These are worth knowing before reading any screen, because every screen assumes them.</p>

<p><b>1. A refusal must never render as "nothing here."</b> Every screen goes through
<code>LoadStateView</code>, and loading / empty / refused / session-ended / rate-limited are
visually distinct states. An empty list and a server saying no are different facts, and a UI that
shows the same grey nothing for both teaches people to distrust it.</p>

<p><b>2. Website features map to native counterparts.</b> Calendar to EventKit, locations to
MapKit, uploads to the native camera and PhotosPicker, reports to PDFKit, sharing to the share
sheet. If the website does it in HTML, the app does it the iOS way rather than wrapping a page.</p>

<p><b>3. One URL space, two front ends.</b> <code>DeepLinkParser</code> maps the website's route
table onto native screens, so <code>ishaunted://</code> and eventually
<code>https://ishaunted.com/...</code> links land on the right screen.</p>

<p><b>4. The C# client patterns are the specification.</b> <code>TokenSession</code> ports the
website's bearer-token handler — single-flight refresh, expiry minus 30 seconds — and response
mapping ports <code>WebApiClient</code> rule for rule: 401 means the session ended, 403 does
not.</p>
"""),

    ("The feed", "10-feed", """
<p>The app opens here. The feed is the front door: posts from groups and investigators, with
media, hashtags, mentions and a group-verified badge.</p>

<p>Three lanes — <b>For You</b>, <b>Latest</b>, <b>Following</b>. "For You" is ranked by a
category-match model whose weights are re-fit from labelled examples on the server, so it changes
as the database grows rather than being a fixed rule.</p>

<p class="note">Every image in this document is generated. Feed media here is simulated so the
screenshots show a populated app; the frame captions say <span class="mono">SIMULATED</span>.</p>
"""),

    ("Notifications", "12-notifications", """
<p>Bell in the top left. Notifications are server-side records rather than push-only, so the list
is the same whether or not the person allowed push, and a device that was switched off misses
nothing.</p>
"""),

    ("Cases", "20-cases", """
<p>A case is the unit of work: a client, a place, a history, and everything gathered about it.
This list is what the signed-in person may see, which is not the same as everything that exists —
the server decides, and the app shows what it is given.</p>
"""),

    ("Inside a case", "21-case-detail", """
<p>The case detail carries the original request, the timeline, files, investigations, and the
people involved. Tabs are the same subjects the website uses, so somebody who knows one knows the
other.</p>
"""),

    ("Case messages", "22-case-messages", """
<p>Messages between the group and the client, in the case they belong to rather than in a separate
inbox. A message that cannot be read in context is worth much less.</p>
"""),

    ("Field Kit — the reason the app exists", "40-fieldkit-home", """
<p>Everything above has a website equivalent. <b>This does not.</b> The Field Kit turns the phone
into an instrument, and it is the part a new developer should read first and most carefully.</p>

<p>The home screen lists past sessions and starts a new one. A session is a recording in the
fullest sense: not a single measurement, but <b>every reading from every enabled sensor, for as
long as it runs</b>.</p>
"""),

    ("Naming a session", "41-name-the-session", """
<p>A session is named before it starts, not afterwards. The name is how it is found later, and
asking at the end — when somebody is tired and packing up — is how recordings end up called
"Session 4".</p>
"""),

    ("The live session", "42-live-session", """
<p>The instrument. The dial is a magnetometer readout; below it are sound, and whatever else is
enabled.</p>

<p class="rule"><b>The needle is missing on purpose until you set a base level.</b> It shows the
<b>departure from base</b>, not the absolute field, because the absolute number means nothing on
its own — the Earth alone reads about 500 mG and every building bends that. Before a base exists
there is no departure to point at, so the dial says "Set a base level" instead of sweeping a scale
nobody can interpret.</p>

<p>The needle is also damped rather than snapping to each sample. A real meter's movement has
mass, and more usefully, a needle that jitters at 10 Hz is unreadable in the dark — which is the
only place it is ever read.</p>

<p>The caption under the dial is deliberate too: <i>"Magnetic field only — this is not an AC
electromagnetic meter."</i> The app says what the instrument is not, because a person holding it
in a basement cannot check.</p>
"""),

    ("With a base level set", "42b-meter-with-needle", """
<p>Once a base is taken, the needle appears and the readout becomes a signed departure from it —
<span class="mono">+12 mG from base</span> — with the report threshold marked on the dial. A needle
pegged at the stop turns red rather than resting quietly at the maximum, because a reading at the
limit is a reading whose real value is unknown.</p>
"""),

    ("Everything the live session offers", "43-controls-with-base-set", """
<p>Scrolling the live session reveals the rest of the instrument. Each control, and what it is
for:</p>

<table>
<tr><th>Control</th><th>What it does</th><th>Example</th></tr>
<tr><td><b>Position</b></td><td>Live GPS with its accuracy and altitude change. Accuracy is shown,
not hidden, so a reading taken at &plusmn;28 m is not later mistaken for a precise one.</td>
<td><span class="mono">36.16280, -86.78137 &plusmn;28 m</span></td></tr>

<tr><td><b>Reset base</b></td><td>Takes the current field as the new normal. Used on entering a new
room, because "normal" is a property of the room, not the building.</td>
<td>Move from the hall into the cellar, reset, and the dial answers the cellar's question.</td></tr>

<tr><td><b>Mark</b></td><td>Stamps this instant in the recording. Marking is cheap and reversible;
it is the thing to press when something happens and there is no time to type.</td>
<td>A door moves — press Mark, describe it later from the replay.</td></tr>

<tr><td><b>Note</b></td><td>A typed or dictated note, timestamped and attached to the session.</td>
<td>"Three knocks, low on the wall, north side."</td></tr>

<tr><td><b>EVP</b></td><td>Ask a question and have the asking recorded as a marker, so the answer
window is findable later instead of being hunted through an hour of audio.</td>
<td>"Is anyone here?" — the question is marked, the following silence is where to listen.</td></tr>

<tr><td><b>Photo / Video</b></td><td>Capture into the session. Anything taken is stamped with where
you were and which room you had named.</td><td>A photo of the stairs, filed to "Cellar stairs".</td></tr>

<tr><td><b>Audio</b></td><td>Continuous recording for the session, which is what EVP markers point
into.</td><td>Runs for the whole session unless switched off.</td></tr>

<tr><td><b>Watching</b> (sentry)</td><td>Put the device down and let it watch. Anything past your
thresholds is marked automatically for you to find later.</td>
<td>Leave it on a landing during a break; come back to marks you did not have to be present
for.</td></tr>

<tr><td><b>Room</b></td><td>Names where you are. Everything recorded afterwards says which room it
came from.</td><td>"Cellar stairs" then "Boiler room".</td></tr>

<tr><td><b>Blackout</b></td><td>Kills the screen to near-black while continuing to record. A lit
phone ruins both night vision and the video.</td><td>Tap to darken; tap again to return.</td></tr>

<tr><td><b>Sensor toggles</b></td><td>Each sensor can be switched off, with its battery cost stated
plainly — "Barely touches the battery" against video's heavier cost.</td>
<td>Long overnight sit: magnetic field and audio on, video off.</td></tr>
</table>
"""),

    ("Marking, and notes", "44-marked", """
<p>A mark is a moment; a note is a moment with words attached. Both land in the same timeline as
the sensor readings, which is what makes the replay coherent — the readings, the marks, the notes,
the photographs and the audio are one record, not five.</p>
"""),

    ("Writing a note", "45-note-composer", """
<p>Notes can be dictated, because typing in the dark with gloves on is not realistic. The note is
stamped with the time and the current room.</p>
"""),

    ("How a recording is replayed — and why", None, """
<p class="rule"><b>The Field Kit records every reading, continuously, for the whole session — and
selecting a past session plays the entire recording back as it happened. This is deliberate and it
is the central idea of the feature.</b></p>

<p>It does not show you one reading at a time, and it does not ask you to pick a measurement out
of a list. It replays the session: the dial moves as it moved, the marks arrive when they arrived,
the notes appear when they were written, and the audio and video run alongside them on the same
clock.</p>

<p>The reason is that a single reading proves nothing. "47 mG at 02:14" is an anecdote. What
matters is what the field was doing for the ten minutes around it, whether anybody was moving,
what the room was, and what was heard at the same moment — and the only way to judge that is to
watch it happen again.</p>

<p><b>The whole recording can then be submitted as evidence</b>, not just a clip or a screenshot.
It attaches to a case or is published to a place's archive, and it plays back in a player in the
web app — the same replay, in the browser, for people who were not there. That is what turns a
night's work into something a client, a colleague or a stranger can actually assess.</p>
"""),

    ("Ending a session", "53-session-review", """
<p>Stopping opens the review: what was captured, how long it ran, what was marked. From here the
session can be exported, attached to a case, or published to a public place's archive.</p>
"""),

    ("Events", "60-events", """
<p>Public events — tours, open investigations, group meetings. Someone who attended can offer what
they captured; a group member reviews it, and an accepted submission becomes part of that event's
public record, credited to them.</p>
"""),

    ("Profile and account", "70-profile", """
<p>Account, security, environment, and the About and Privacy screens Apple requires to be
reachable without an account.</p>

<p>The app deliberately shows somebody only what applies to them. A person investigating alone
does not see group administration; someone who is not on a ghost walk does not see its
controls.</p>
"""),

    ("My evidence", "71-my-evidence", """
<p>What this person has captured and offered, and what became of it. A guest who photographed
something at somebody else's event keeps their own copy and can see whether it was accepted.</p>
"""),

    ("Security", "72-security", """
<p>Password, two-factor, sign-out, and account deletion. Deletion anonymises rather than shredding
where a record must survive — and the server refuses to delete a group's last owner, because an
ownerless group is worse than a deleted account.</p>
"""),

    ("Building and running it", None, """
<p>From <code>Ben.iOS/</code>:</p>
<pre>./scripts/test.sh                          BenKit unit tests, host only, no simulator
./scripts/build.sh                         unsigned simulator build (CI compile check)
./scripts/run-sim.sh                       build, install and launch on iPhone 17 Pro
./scripts/run-sim.sh "iPad Pro 13-inch (M5)"
OPEN_LINK="https://ishaunted.com/events" ./scripts/run-sim.sh    deep-link on launch</pre>

<p>Run the API first — <code>dotnet run</code> in <code>Ben.Data.WebApi</code> on :5252 — and pick
<b>Dev</b> under Profile &rarr; API environment. The simulator reaches the Mac's localhost
directly.</p>

<p class="rule"><b>One trap worth knowing.</b> A fully unsigned build
(<code>CODE_SIGNING_ALLOWED=NO</code>) cannot use the simulator Keychain, so token persistence
silently fails and every launch looks signed-out. Only <code>build.sh</code> disables signing;
<code>run-sim.sh</code> and Xcode use ad-hoc signing on purpose.</p>

<p>Screenshots for this document are captured by
<code>IsHauntedUITests/DeveloperDocCaptureTests</code>, which needs its flag passed as a shell
environment variable — <code>TEST_RUNNER_BEN_DOC_SHOTS=1</code> before <code>xcodebuild</code>, not
as a build setting, or the test silently skips.</p>
"""),
]

CSS = """
@page { size: A4; margin: 16mm 14mm; }
body { font: 11pt/1.55 -apple-system, "Helvetica Neue", Arial, sans-serif; color: #16181c;
       margin: 0; }
h1 { font-size: 26pt; margin: 0 0 2mm; letter-spacing: -0.4px; }
h2 { font-size: 15pt; margin: 9mm 0 2mm; padding-top: 3mm; border-top: 1px solid #d8dce2;
     page-break-after: avoid; }
.sub { color: #5b626d; font-size: 11pt; margin: 0 0 6mm; }
p { margin: 0 0 3mm; }
code, .mono, pre { font-family: ui-monospace, Menlo, monospace; font-size: 9.5pt; }
code { background: #f1f3f6; padding: 0 3px; border-radius: 3px; }
pre { background: #f1f3f6; padding: 3mm 4mm; border-radius: 4px; white-space: pre-wrap;
      line-height: 1.45; }
.rule { border-left: 3px solid #2e6b34; padding-left: 4mm; background: #f4f8f4; padding-top: 2mm;
        padding-bottom: 2mm; }
.note { color: #5b626d; font-size: 9.5pt; }
figure { margin: 4mm 0; page-break-inside: avoid; text-align: center; }
figure img { max-height: 155mm; max-width: 78mm; border: 1px solid #d0d5dc; border-radius: 8px; }
figcaption { color: #5b626d; font-size: 9pt; margin-top: 2mm; }
table { border-collapse: collapse; width: 100%; margin: 3mm 0; font-size: 9.5pt; }
th, td { border: 1px solid #d8dce2; padding: 2mm 2.5mm; text-align: left; vertical-align: top; }
th { background: #f1f3f6; }
.cover { border: 1px solid #d8dce2; border-radius: 6px; padding: 5mm; background: #fafbfc;
         margin-bottom: 6mm; }
"""


def main():
    parts = [f"<!doctype html><html><head><meta charset='utf-8'>"
             f"<title>IsHaunted {DEVICE_NAME} — developer guide</title>"
             f"<style>{CSS}</style></head><body>"]
    parts.append(f"<h1>IsHaunted for {DEVICE_NAME}</h1>")
    parts.append("<p class='sub'>What the app does, how it is built, and how to run it — "
                 "written for a developer joining the project.</p>")
    parts.append(
        "<div class='cover'><p style='margin:0'><b>Every screenshot in this document shows "
        "simulated data.</b> The accounts, cases, feed posts and recordings are seeded; the "
        "photographs and audio waveforms are generated, and each is captioned "
        "<span class='mono'>SIMULATED</span>. Nothing here is a real investigation or a real "
        f"person. Screens were captured on a {DEVICE_NAME} simulator in dark mode.</p></div>")

    missing = []
    for heading, prefix, body in SECTIONS:
        parts.append(f"<h2>{heading}</h2>")
        parts.append(body)
        if prefix:
            path = shot(prefix)
            if path:
                parts.append(f"<figure><img src='{path}'>"
                             f"<figcaption>{heading} &mdash; {DEVICE_NAME}, dark mode"
                             f"</figcaption></figure>")
            else:
                missing.append(prefix)

    parts.append("</body></html>")
    out = os.path.join(DOCS, f"ishaunted-ios-{DEVICE}.html")
    with open(out, "w") as f:
        f.write("\n".join(parts))

    print(f"wrote {os.path.relpath(out)}")
    if missing:
        # Named rather than swallowed: a section without its picture is a section the capture run
        # never reached, and that is worth fixing rather than shipping around.
        print(f"  no screenshot for: {', '.join(missing)}")


if __name__ == "__main__":
    main()
