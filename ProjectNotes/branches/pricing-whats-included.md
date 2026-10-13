# Pricing: what each plan includes, and hosted events (10/13/2026)

Ben, 10/13/2026: *"On the FREE version in the prices, it says "No Limits" where the others say "Up to
X open cases" and this can be misleading. It should say "No limits on public posts" or something
like that. Also, can we put a small button on each of the levels to show what is included with each
of them? Remember to create it based on whatever is enabled or disabled. I just turned on the Video
Editor, so it would now display it for each plan... Instead of saying what the next plan up includes,
say everything from the personal plan and list the differences... like the increase in amount of
storage etc. This can be on the same page, but below all the pricing bands. Also, include the
purchase ability of one or more "events" with a description of what the events include... all the
benefits including the menu and room seating creation etc."*

Branch `feature/pricing-whats-included`, from develop.

## What was built

1. **The free band's line.** A band with no caps used to say "No limits". On the free band it now
   says "No limits on public cases and posts", since a free group's work at a public place is public
   and its private work is the paid part. A paid band with no caps still says "No limits".
2. **"What's included" on every band**, linking to `pricing#included-<tier id>`. The site's base
   address is `/`, so a bare `#…` link went to the home page; the test caught it.
3. **What each plan includes** (`Billing/PlanInclusions.razor`), below the bands.
   - The first band is listed in full, under "Every plan includes", from the site's feature
     switches. Turning the video editor on adds it for every plan; switching it off removes it.
     Store is left out, because it's the site's shop, not part of a plan.
   - Then "On <first>": its people, the privacy rule, storage, caps and capabilities.
   - Every other band reads "Everything in <first>, with these differences", compared with the
     first band, not with the band below it.
   - A cap that only one of two plans sets is shown as "unlimited" on the other.
4. **Hosted events**, below that:
   - **What an event includes:** the rooms or seats plan, bookings and holds, the booking board,
     the program, menus, dining tables, the dietary sheet, passes and the door, wristbands, staff
     rights, the public page and gallery, files, the guests' room, the thank-you, reviews and the
     list of who came.
   - **Settings shown:** per-event storage and how long files are kept, and that the site never
     takes guests' money.
   - **Buying:** the price per event, plus tax, and the rules: paid at first publish, drafts free,
     good for 365 days, returned when cancelled 48 hours or more ahead, at most 20 at once.
   - **Who sees the buy panel:**
     - A billing person for a group whose plan lacks hosting can buy one or more there, with a
       group chooser when they run several. This goes through the same checkout as the billing
       page.
     - A visitor gets "Sign in to buy events".
     - Someone signed in without a group gets "Start a group".
     - A group whose plan already includes hosting is told it has nothing to buy.
5. **`GET api/public/pricing/extras`** (anonymous). It returns the free account's storage cap and
   the event offer: on sale, price, life, the most per purchase, the cancellation window, storage,
   and how long files are kept. Each value is read the way the enforcing code reads it, with the
   same fallbacks.
6. **`Billing/PricingWords.cs`** holds the wording for all 17 caps, shared by the bands and the
   comparison. The bands used to show the raw enum name for nine of them. Caps nothing enforces
   (storage, custom roles, event sessions, staff, files) aren't used to describe a plan.

## The live data, and what it means here

Production (10/13): Free (1 person, $0), then Small, Standard, Large and Enterprise, capped only on
open cases (2, 5, 12, 25).

- Free lacks only private-residence cases.
- **Every band includes hosted events**, so the page says "Every plan includes hosted events, so
  there's nothing to buy" and shows no buy panel.
- For Free groups to buy events, untick Host events on Free in Administration → Price Bands. The
  page then offers them at the event credit price.
- Free has no open-case cap and the paid bands do, so the paid cards show "Up to N open cases" as a
  difference. That's the data as it stands.
- Bands carry nine of the ten role areas (not Events), so the comparison says custom roles can't
  give rights over events.

## Tests

- **Unit:** `PublicPricingExtrasTests` covers the defaults, the settings being what the page shows,
  sales switched off, and a nonsense price falling back to the one checkout charges.
- **Browser:** `PricingWhatsIncludedTests` checks:
  - each band jumps to its own plan;
  - the list follows the feature switches, read from the API;
  - events show what they include and their price, or that plans include them;
  - the free band no longer says a bare "No limits".
- All of these were run against `IsHauntedDb_pricing`, a side database shaped like production with
  hosting taken off Free, as well as the standard e2e database.

## Docs

The organization-administration help (Your group's plan) covers What's included and Hosted events,
with two new screenshots. The product PDF and the owner seat guide are rebuilt.
