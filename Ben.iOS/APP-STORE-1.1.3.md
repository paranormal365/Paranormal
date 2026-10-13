# App Store update — IsHaunted 1.1.3 (build 12)

The violet icon, a Profile check for updates, and event menus drawn in the designs their hosts chose.

| | |
|---|---|
| Version | **1.1.3** (`MARKETING_VERSION`) |
| Build | **12** (`CURRENT_PROJECT_VERSION`) — the highest ever archived or uploaded was 11 (1.1.2) |
| Replaces | 1.1.2 (11), released 2026-10-03 |
| Archived | 2026-10-13, from develop |
| Server work first | **Yes** — deploy the website and API first (see below). |
| Screenshots | No change needed. The store screenshots don't show the icon or the menus. |

## 1. What changed

- **The icon** is violet, to match the website (Ben, 10/02).
- **Profile → App** shows the version and checks `api/public/app-version/ios` for a newer one, with
  **Update in the App Store** and **Check for updates** (Ben, 10/03).
- **Event menus** are drawn in their own designs: paper, inks, frame, ornament and typefaces, the same
  words as the website, and **PDF** in Quick Look. Published menus are public, so the event's page
  links to them for anyone, signed in or not. See `ProjectNotes/branches/ios-designed-menus.md`.

## 2. Deploy the website and API first

The menus need `GET api/public/menu-designs`, `api/public/menu-fonts/{file}` and
`api/public/hosted-events/{id}/published-menus`, which are new. Against the live API as it is today, the
menus screen says there are no menus. The deploy also owes these migrations: **MemberPublicListing**,
**RenewalGracePeriod** and **EventMenuDesigns**.

## 3. Ship it

1. Deploy the website and API (section 2).
2. Xcode → Window → **Organizer** → the **IsHaunted 1.1.3 (12)** archive → **Distribute App** →
   App Store Connect → **Upload**. Leave "Manage version and build number" **off** (the wizard's default
   of on is how 1.0.2's build 3 became 4).
3. App Store Connect → IsHaunted → **+ Version** → **1.1.3**.
4. **What's New in This Version**:

   > A new violet icon.
   >
   > Profile shows which version you have and tells you when a newer one is in the App Store.
   >
   > Event menus now look the way the hosts designed them, just as on the website, and open as a PDF to
   > save or share. Anyone can see an event's published menus, without signing in.

5. Select build **12**, **Add for Review**.

## 4. When it's live

Follow `APP-STORE-1.1.3-HELP.md`: put back the held-back help (the update check and the designed menus,
with the new menus picture in `help-1.1.3/`), rebuild the product PDF, and swap the green icon on the
home page, the QR code and the app guide for the violet one.

If Apple rejects it for code, the next build is **13**. An uploaded number is never reused.
