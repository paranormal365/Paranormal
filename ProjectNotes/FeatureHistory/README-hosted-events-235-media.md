# Item 235 — demo content and photographs (phase 17c)

Ben asked on 2026-09-13 for free photographs, *"aligned with the topic, fitting the allotted area"*, for the tickets,
ads, menus, events and venues. He reviews them once the documents exist and says which to replace.

## The photographs

- **Source:** 24 photographs from [Unsplash](https://unsplash.com), under the
  [Unsplash License](https://unsplash.com/license). Use is free, including commercially, and attribution is not
  required, but it is recorded here anyway.
- **Checked:** each photo's page reported `premium: false` and `plus: false`, so none is Unsplash+.
- **Downloaded:** 2026-09-13, at 2000 px wide, 14.0 MB in total. Ben approved the download.
- **Kept in the repository:** only the resized copies the documents use, under `docs/media/stock/` (phase 17e).

| File | Photographer | Size | For |
|---|---|---|---|
| V1-venue-exterior.jpg (unsplash.com/photos/e8AQUdHsxmo) | T (tanyabarrow) | 0.7 MB | venue & hotel event pages, venue ad |
| V2-victorian-mansion.jpg (unsplash.com/photos/_yW_pooWG9Y) | Zoshua Colah | 1.2 MB | venue & hotel event pages, venue ad |
| V3-hotel-lobby.jpg (unsplash.com/photos/4U1m4QPudPk) | Jeremy Wilkinson | 0.6 MB | venue & hotel event pages, venue ad |
| V4-hallway-chandelier.jpg (unsplash.com/photos/gAYt5Ru6fZM) | Strange Happenings | 0.4 MB | venue & hotel event pages, venue ad |
| V5-hotel-bedroom.jpg (unsplash.com/photos/taKLGJzEn14) | Strange Happenings | 0.3 MB | venue & hotel event pages, venue ad |
| V6-ballroom.jpg (unsplash.com/photos/krAQD5gGBSM) | mana5280 | 0.8 MB | venue & hotel event pages, venue ad |
| V7-hotel-bar.jpg (unsplash.com/photos/DbMZgyFrycw) | Andrea De Santis | 0.9 MB | venue & hotel event pages, venue ad |
| D1-candlelit-dinner.jpg (unsplash.com/photos/UEjjO-aJtZ8) | Romain Gal | 0.3 MB | menus, dining, event gallery |
| D2-candle-table.jpg (unsplash.com/photos/9jNYTFpa2gs) | Sara Abilova | 0.5 MB | menus, dining, event gallery |
| D3-plated-steak.jpg (unsplash.com/photos/MaWMfm-HCqQ) | Urban Gyllström | 0.2 MB | menus, dining, event gallery |
| D4-plate-and-wine.jpg (unsplash.com/photos/dhxtgSdwgyo) | liuyun wu | 0.6 MB | menus, dining, event gallery |
| S1-candles.jpg (unsplash.com/photos/fvl4b1gjpbk) | Mike Labrum | 0.2 MB | séance programme, event ads |
| S2-ouija.jpg (unsplash.com/photos/7YZupa9tAcU) | Josh Olalde | 0.4 MB | séance programme, event ads |
| S3-candle-chandelier.jpg (unsplash.com/photos/aiIe4fBhLWU) | Kamilla Isalieva | 1.3 MB | séance programme, event ads |
| T1-theatre-seats.jpg (unsplash.com/photos/OaVJQZ-nFD0) | Denise Jans | 0.2 MB | seated 'Evening of Evidence' event |
| T2-audience-presentation.jpg (unsplash.com/photos/uYLBBm4y_qA) | Ufoma Ojo | 0.5 MB | seated 'Evening of Evidence' event |
| W1-cobblestone-streetlights.jpg (unsplash.com/photos/CRygBKK6IAY) | T (tanyabarrow) | 1.4 MB | ghost walk tours ad |
| W2-cobblestone-night.jpg (unsplash.com/photos/ra-Dyg0FLUs) | Jan Kopřiva | 1.3 MB | ghost walk tours ad |
| W3-foggy-graveyard.jpg (unsplash.com/photos/gYC68k9trV8) | Rodion Kutsaiev | 0.3 MB | ghost walk tours ad |
| I1-flashlight-silhouette.jpg (unsplash.com/photos/OpDJtIAgUrQ) | Daniel Gomez | 0.2 MB | investigators/individuals & groups ads |
| I2-dark-hallway.jpg (unsplash.com/photos/t7BOZp77S6c) | Miguel Alcântara | 1.0 MB | investigators/individuals & groups ads |
| I3-walking-to-house.jpg (unsplash.com/photos/z9hvkSDWMIM) | Ján Jakub Naništa | 0.3 MB | investigators/individuals & groups ads |
| P1-phone-camera.jpg (unsplash.com/photos/cDGWgZdqHWY) | dominik hofbauer | 0.2 MB | iPhone app ad |
| P2-holding-phone.jpg (unsplash.com/photos/1rJdewJPCWE) | Shubham Sharan | 0.1 MB | iPhone app ad |

**Weakest fits, to raise with Ben:**
- **T2** is a brightly lit conference hall, which breaks the dark look.
- **D4** is a white plate on a white cloth.

## What was put on `IsHauntedDb_player`

Done through the API as the SuperAdmin, so every picture went through the site's own fitting and metadata
stripping:

- **Thomas House Séance Weekend** (Paranormal365, rooms):
  - gallery V1, V6, V4, V5, D1, S3, D3, with captions; V1 leads;
  - published, covered by the group's plan.
- **An Evening of Evidence** (Paranormal365, seats):
  - gallery T1, S1, S2, T2; T1 leads;
  - published.
- **The Thomas House Hotel venue profile:** photos V1, V3, V6, V4, V5, V7.
- **Printers Alley Walks → Church Street Walk** (tour `pw-tour-1789070429`): gallery W1, W2, W3.
- **Paranormal365's subscription** had lapsed on 09/12, so nothing could be published. On the testing copy it
  was set back to *Active* on its own Small group band, monthly, for 140 days. The period snapshot was written,
  and no ledger charge.
- **Two event credits** were granted to Paranormal365, with a reason naming this phase. They are unspent,
  because the plan covered both events. They are kept for the removal walk.
- **Four leftover test events** named *Thomas House Weekend 2323xx*, published under BenCo by earlier runs, were
  archived. That can be undone.

The seeder also fills the access notes on both demo events (phase 17a), so fresh databases get them.

I3, I1, I2, P1, P2, S1, S2 and the ghost-walk photos are for the advertisements and the Hosted Events PDF (17e).
