# Item 252: the lead starts everybody's Field Kit (09/28/2026)

Branch `feature/lead-launch-252`, from develop after the iOS time zones (`76247740`). Ships in the
app's **1.1.0**. Ben:

> check to see if the tour guide, event planner or group investigation lead is logged in and if they
> are, they should be able to send a push from the app to others logged in who are registered for
> the event, tour etc and pushes their phone app to automatically start with the event, tour
> investigation in the field kit... They can take multiple sessions without having to do anything
> with them. They can choose to submit them later. They will be limited to 10 minutes each or
> 550mb for upload, so they might have to clip the time.
>
> maybe the guide or planner has a button that submits to the server and the server pushes to the
> participants.
>
> It might be public, it might be a single investigation in a larger case, it might be a public
> investigation at a public event, it might be a tour or a specific event and the planner wants the
> hunt to start at a specific time. That is what the button does. I think it should also write a
> text message in the feed the users can click to start a new session if they miss the first push.
> This will expire like 6 hours after the end of the event or tour or investigation.

## Decisions (Ben, 09/28/2026)

0. **Nobody is forced into a session** (Ben, later the same day): "Maybe instead of forcing their
   phone into a session, the launch button posts the link in the feed where they can click to join
   the group's session... Clicking the link would open the Field Kit into a session where they don't
   have to complete picking the location before the session." / "Instead of forcing them to join."
   The card's **Join** (and the push, which only says it is starting) opens Field Kit straight into
   a session already set to the event, tour or investigation and its place; recording starts when
   they press Start. **A push goes out as well** (Ben chose "feed link + push").

1. **Feed post audience:** public when the thing is public (a public tour date, public event,
   public investigation); otherwise only the people it was sent to, and the lead.
2. **Timing:** the button starts the hunt when it is pressed. No scheduling in advance.
3. **Upload limit:** stays 600 MB and 10 minutes of video per upload (not 550).
4. **Who may launch:** the lead plus the group's managers —
   - a tour date: its guides, and anyone who may edit the group's calendar;
   - a hosted event: its organiser, and staff who run the door;
   - a public calendar event: anyone who may edit the group's calendar;
   - an investigation: its lead, its creator, the case manager, and group admins
     (`InvestigationAccess.CanManageAsync`).

## Where things stood (mapped 09/28)

- **No push at all.** No APNs sender, device-token table or endpoint on the server; no
  `aps-environment`, no `registerForRemoteNotifications`, no tap handler in the app. (The backlog
  said "the app registers for pushes"; it does not. Seat reminders are LOCAL notifications.)
  Reusable: the ES256 `.p8` signing Sign in with Apple already does (`AppleClientSecret`,
  `PrivateKeyFile`).
- **A Field Kit session belongs to an investigation or nothing.** No event or tour-date field on the
  phone's `FieldSession`, the upload form, or `FieldSessionUpload`.
- **Feed posts** (`OrgMessage`, PublicFeed) have no expiry, no audience, and no event / tour /
  investigation target; there is no robot account (posts are written as a person).
- **Registered** means: tour dates and public events — `OrgCalendarEventAttendee.RsvpStatus =
  Accepted`; hosted events — a Confirmed booking's lead and named guests; investigations —
  `InvestigationAttendee` rows not declined, and live guest passes.
- **No "things I lead" list** in the app for tour dates or events; guides are display rows only.
- **Found in passing:** sending a second ten-minute window of one session REPLACES the first on the
  server (same device session id), and a session row does not say whether it has been sent.

## Plan

**L1 — Push, end to end (server).** `PushDevice` (user, APNs token, environment, app version,
last seen); `POST/DELETE api/me/push-devices`; an APNs sender (HTTP/2, ES256 JWT from a `.p8`,
config section `Apns`, sandbox vs production per token; 410 Unregistered prunes the token); a fake
sender for tests. Never logs a token.

**L2 — Launches (server).** `FieldLaunch` (target: investigation, calendar event / tour date, or
hosted event; launched by/at; ends at; expires = end + 6 h; public or not). `POST
api/field-launches` checks who may launch and fans the push out to registered people with a
device; `GET api/field-launches/mine` lists live ones. The feed post: `OrgMessage` gains
`FieldLaunchId` and `ExpiresUtc` — public when the target is public, otherwise shown only to the
launch's people. Uploads accept an `orgCalendarEventId` / `hostedEventId` target, with a rule for
who may attach.

**L3 — Joining, in the app.** The feed card's **Join**, a "Happening now" list in Field Kit, and a
Field Kit link (`ishaunted://field-kit/launch/{id}`) all open the live session pending (item 215),
already set to the thing and its place — no New session sheet. Push: entitlement and delegate,
token registered when signed in and removed at sign-out; a tap opens the same link.

**L4 — The lead's button (app).** "Things I lead today" and a **Launch** button on an
investigation, a tour date and an event door, with "Send to N people" before it goes.

**L5 — Sessions pile up (app + server).** Sessions tied to a tour date or event on the phone and in
the `.ben` upload; each row says sent / not sent; the next window of a long session adds to the
first rather than replacing it.

**L6 — Where the sessions land (website).** A tour date's and an event's sessions are visible to
the group that ran it, and the feed shows the launch post on the website too.

**L7 — Help, change logs, screenshots, PDF; tests throughout.**

## Needs Ben (Apple Developer account)

An **APNs key**: Certificates, Identifiers & Profiles → Keys → + → *Apple Push Notifications
service* → download the `.p8` once, note its Key ID. And the **Push Notifications** capability on
the `com.ishaunted.ios` App ID (Xcode's automatic signing adds it with the entitlement). Until
then everything is built and tested with a fake sender and `xcrun simctl push`.
