# Event menus: an item library, designed menus, previews, publishing and PDFs (10/13/2026)

Branch `feature/event-menus`, from develop at 54dedb85.

## What Ben asked for (10/13/2026)

The spec he pasted, in short:

- **Menus are drafts until published.** A menu has a title only the planner sees. It has no meal
  (breakfast, brunch, lunch, supper, dinner, snacks) until one is chosen.
- **Items under categories.**
  - Categories (Appetizers, Dishes, a named course, Sandwiches, Snacks, Drinks…) are one list for
    the whole site.
  - A planner can add a missing category. Names must be unique, and a new one is offered to every
    event after.
  - Each item has a name, an optional description, an image, a category and allergen/vegan-style
    notes.
- **A menu is built from those items.**
  - Categories become sections, and items are added under them. Both sections and items can be
    reordered.
  - Each item on a menu is one of three prices:
    - included in the meal's price;
    - covered by the event's price;
    - priced on its own. The price is then required: an extra like blue cheese on a steak, or
      alcohol above the menu or event price.
  - A meal price lives on the menu: one amount, or several (adults, children, seniors, first
    responders…).
  - If an item is covered by the event's price, the event's ticket information says so.
- **Designs.** The planner picks from many ready-made looks, some built around ghost hunting and
  some general, and can add a logo or image.
  - Every design ends with a very small credit: IsHaunted.com, perhaps the logo, and "© {year}
    ISHAUNTED LLC".
- **Dates.**
  - A menu is assigned to dates of the event and can be used more than once.
  - It can be copied as "<name> Copy 1", numbered upward until renamed, and changing the copy
    leaves the original alone.
  - More than one menu can be published for a meal: a children's menu, a vegan menu.
  - An event may have no menus or many.
- **Where menus appear.** Published menus show on the event's calendar on the site, and can be
  downloaded as a PDF (Telerik is fine).

Then, in answers and follow-ups:

- **Where items are saved.** The planner chooses for each item:
  - the group, for things it always brings (pizza);
  - the venue, for what the venue keeps (Thomas House's own menus);
  - this event only.
  The difference is explained on the form.
- **Who sees published menus:** everyone, on the event's page and calendar. Drafts stay private.
  (This replaces the old confirmed-guests-only rule.)
- **Meals stay the schedule.** The event's meals (date, meal, time) remain what the dining tables
  are attached to. A menu is attached to one or more meals, and a meal can show several menus.
- **Preview** the menu at any time while building it, and switch designs to compare.
- **Demo menus** in every design, so people can see a finished one.
- **Whole-event offerings.** Candy at $2 or bottled water and soft drinks for the whole event, or
  the same things free with the event. Handled as a menu marked **Throughout the event** instead
  of attached to meals; each item there is priced or covered by the event's price.

## What's there today, and what it means here

- **Meals.** `HostedEventMenu` is one meal (a "sitting") of one night: title, time and notes, with
  `HostedEventMenuItem` dishes typed as "Course: Name" lines. The dining tables'
  `HostedEventDiningSeat.HostedEventMenuId` points at it, and deleting it removes the seats.
  **It stays, as the meal**, so dining is untouched; the screen calls these meals, not menus.
- **Problems found while mapping, fixed along the way:**
  - The web editor drops each dish's description and dietary tags on every save (only name and
    course go back). The new item library replaces that box.
  - Website guests never see a menu. Only the iPhone app reads `GET api/public/hosted-events/{id}/menus`,
    and only for a confirmed lead booker. The iOS app keeps working: that endpoint keeps its shape,
    filled from the published menus.
  - Kitchen staff with "Menus and the kitchen's sheet" pass `CanEditMenusAsync`, but
    `HostedEventMenuController` checks membership and event-edit instead, so they can't open
    menus. The new controllers use `CanEditMenusAsync`.
- **PDF.** `Telerik.Documents.Fixed` is already used for case reports. There are no TTF fonts in
  the repo (web fonts are woff2, which Telerik can't embed), so the PDF designs use the standard
  PDF families: Times, Helvetica and Courier. Each design's colors, rules, ornaments and layout
  carry its look. Adding licensed display fonts later is optional and needs font files downloaded.
- **Venue ownership.** No group owns a place. The verified `OrganizationVenueProfile` at the place
  is its venue group, and `VenuePhoto` is the precedent for things kept at a venue. Items saved to
  a venue:
  - are offered to every event at that place;
  - can be edited by the group that saved them and by the place's verified venue group.

## The model

Every new table is append-only with audit fields like the rest.

- **`MenuCategory`** is site-wide.
  - It has a name (unique, ignoring case and spacing) and a sort order for the default section
    order.
  - It's seeded with the common categories, and a planner can add one.
- **`MenuLibraryItem`** belongs to a group and has a scope:
  - Event: needs the event;
  - Group;
  - Venue: needs a place, the event's own.
  - It also has a name, a description, a category, an image (`UploadFile`), dietary marks
    (`MenuDietary` flags: vegan, vegetarian, gluten free, dairy free, nut free, spicy, halal,
    kosher), allergens (`MenuAllergen` flags: the nine major US allergens) and a free-text note.
- **`EventMenu`** belongs to an event.
  - It has a planner's title, a meal (optional until chosen), "Throughout the event" (yes or no),
    a public heading and intro, a design key, a logo or image (`UploadFile`), and when it was
    published and unpublished.
  - It also records the menu it was copied from.
- **`EventMenuPrice`** is a menu's meal price: label and amount ("Adults $25", "Children $12"),
  in order.
- **`EventMenuSection`** is one category on a menu, with an optional heading override, in order.
- **`EventMenuEntry`** is one library item in a section.
  - Its price is one of three: included in the meal, covered by the event, or priced on its own.
  - The amount is required when priced on its own, along with an optional note ("add blue
    cheese") and the order.
- **`EventMenuServing`** links a menu to a meal (`HostedEventMenu`), many to many.

The rules:

- **Copying** a menu copies its sections, entries and prices, but not its meals or publication.
  The name is "<name> Copy N", with N the next unused.
- **Publishing** requires:
  - a title;
  - at least one entry;
  - a meal price if any entry is included in the meal;
  - and either a meal or "Throughout the event".
- **Unpublishing** hides the menu again and keeps it.
- **Who may change menus:** `HostedEventAccess.CanEditMenusAsync`, meaning event editors and
  kitchen staff.
- **Who may change library items:** the owning group's event editors, and the verified venue
  group for venue items.

## Designs

A design is a code-side registry entry (`MenuDesigns`): key, name, family (ghost hunting or
general), palette, font family (Times, Helvetica or Courier), title treatment, rule and ornament
style, and layout (one or two columns). The same entry drives:

- the HTML rendering (`EventMenuDocument.razor`, used for the preview, the event page and the demo
  gallery);
- the PDF rendering (`EventMenuPdf`).

Ghost hunting: **Séance Parlor** (Victorian, gold on oxblood), **Midnight Vigil** (navy and
candlelight), **Spirit Board** (sepia talking-board), **Haunted Manor** (gothic black and crimson),
**Case File** (typewriter evidence file), **EVP Static** (modern dark with signal lines).

General: **Classic Bistro**, **Modern Minimal**, **Chalkboard Café**, **Rustic Farmhouse**,
**Garden Party**, **Elegant Evening**.

The footer on every design reads "Menu made with IsHaunted.com · © {year} ISHAUNTED LLC" in very
small type, with the small site mark beside it. It's small enough not to compete with the venue's
own branding, and still says where the menu came from.

## Phases

1. **Model and migration.**
   - Add the tables and the seeded categories.
   - Convert the existing typed dishes: each meal with dishes becomes a published `EventMenu`
     attached to that meal. Its dishes become event-scoped library items, with the course mapped
     to a category (an unknown course becomes a new category).
   - Unit tests.
2. **API.**
   - Categories (list, add).
   - The library (list by scope for an event, add, edit, retire, image upload).
   - Menus (list, get, save, copy, publish, unpublish, attach to meals).
   - Public read: published menus for an event that is on the public site.
   - PDF.
   - The old guest endpoint kept for iOS, filled from the published menus.
   - Unit tests for each rule.
3. **Designs.** The registry, the HTML document and the PDF renderer, with a test that every design
   renders HTML and PDF.
4. **Planner screens.**
   - Meals: the old Menus page, now date, meal, time and note only.
   - The item library, with the scope chooser explained.
   - The menu builder: sections, entries, prices, reorder, meals or throughout, logo, design
     picker and a live preview beside the form.
   - Copy, publish and PDF.
5. **Guests.**
   - The event page shows each date's published menus, plus the "Throughout the event" offerings,
     with a PDF download.
   - The booking card and ticket information say what the event price covers.
6. **Demo menus.**
   - The demo seeder builds a demo event's menus, one in every design.
   - The design picker shows each design filled with sample food.
7. **Docs and the full run.** Help (menus, dining, going to an event), screenshots, product and seat
   PDFs, changelog, then the full e2e run.

## At deploy

One migration: the new tables, the seeded categories and the conversion of existing dishes.

## Built (10/13/2026)

All seven phases, in three commits:
- **22daa347:** the model, the migration and the conversion.
- **2f089498:** the API, the designs and the PDF.
- **d3adb12e:** the screens, guests' menus, the demo menus and the docs.

Changes from the plan, and things found on the way:

- **The builder's preview isn't `position: sticky`.** `.content-wrapper` is `overflow-y: auto` and
  never scrolls, so it becomes the scroll container and sticky never sticks inside it. That breaks
  the event page's own sticky index (`sticky-lg-top`) too, which is left as it is. The editor uses
  two panes that scroll on their own instead.
- **New child rows have to be added to the context by name.** Saving a menu first threw
  `DbUpdateConcurrencyException`. New prices, sections and entries with their ids set, put into a
  tracked menu's collections, were taken for existing rows. `EventMenus.Apply` now adds each row,
  and `EventMenuSaveTests` fails without that fix.
- **Website guests have no way to give dietary notes**: only the organizer's screens and the app
  take them. The guest help says to tell the venue. Not fixed here.
- **Item photos and logos are served by `api/public/menu-image`.** Anyone can see them once the
  menu is published on a public event. Before that, only members of a group that can use them.
  Staff who aren't members don't see draft pictures in the preview.
- **A venue item can be saved by any group holding an event there**, and changed by that group
  and by the verified venue. There's no approval step like the venue photo library's; worth
  revisiting if a venue's list fills with other groups' items.

## Tests

- **Unit:** `Services/EventMenus/` covers the conversion, the seeding, the rules (where items are
  kept, saving, publishing, copy names, categories), what guests and the app read, the PDF in
  every design and over two pages, and saving twice.
- **Browser:** `EventMenuBuilderTests` covers the gallery, the preview following typing and the
  look, publishing refused and then allowed, the event page, the public PDF, and the editor at
  1280 and 375 pixels. `EventMenusTests` was updated for the Meals page.

## Fonts and 25 designs (10/13/2026, `feature/menu-fonts`)

Ben: "add some free custom design fonts available to the menus and update the previews to use the
best looking designs", then "make more designs … 25 or so".

- **Fonts.** 23 free families from Google Fonts: SIL Open Font License, except Special Elite under
  Apache 2.0, with the licenses in `Ben.Data.Common/Menus/Fonts/licenses`. They're static TTFs
  fetched through the Google Fonts CSS service, since the repository only has variable fonts for
  several families. There are 44 files in all, embedded once in Ben.Data.Common.
  - The API registers them with Telerik (`MenuPdfFonts`: regular and bold only, since Telerik has
    no semibold).
  - The website serves `/menu-fonts.css` and `/menu-fonts/{file}` from the same bytes, so a menu's
    page and its PDF use the same type.
- **Designs.** Every design names its heading face, body face, heading weight and scale; there are
  25 in all, 13 for ghost hunting and 12 general.
  - Two new ornaments: Ghost and Bat.
  - The picker lists the strongest first, and new menus start in Séance Parlor
    (`MenuDesigns.Featured`).
  - The gallery draws each sample whole at `zoom: .4`.
  - The demo seeder adds a sample for any design that hasn't got one, so existing demos gain the
    new ones.
- **Tests.** `EventMenusTests` now uses the one-night evening for its meals page. Clearing the
  weekend's meals on every run was knocking the demo's designed menus off their meals.
