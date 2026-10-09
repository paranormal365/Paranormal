---
title: Selling in the Store
summary: Your items in the store — adding them, asking for them to go on sale, and where each one stands.
section: Getting Started
audience: SignedIn
role: Seller
order: 50
---

If you make equipment and sell it through the store, you have a **Selling** section in the menu.
It holds everything to do with your own items. You only ever see your own items, and running the
store itself stays with the store's administrators.

The menu shows **Selling** once the store is open. Before then, the store may ask you to get your
items ready early. In that case, go straight to `/store/selling` — the selling pages work there
while the store stays hidden from everyone else.

## Your items

**Selling → My Items** lists every item you sell through the store, with:

- **Status** — *Draft* (not on sale yet), *On sale*, or *Off sale* (was on sale, but isn't now).
- **Price** — what shoppers pay. The store sets each item's price, so you can see it here but not
  change it. You say what *you* want to be paid when you ask for the item to go on sale.
- **In stock**, **Sold** and **Last sold** — how many are on the shelf, how many have sold, and
  when the last one sold.

The counts at the top show how many of your items are drafts, on sale and off sale.

Press **Add an item** to start a new one: a name and a shelf are enough. It starts as a draft,
hidden from shoppers, and opens in its editor.

![My Items: each item with where it stands, its price and what it has sold](/help/media/selling-in-the-store/my-items.png)

## Editing an item

Open an item from My Items. Its editor has these tabs:

- **Details** — the name, the shelf, a short description, the full description and the
  specifications (grouped under headings like Detection and Power). Press **Save**. The shelves
  listed are the ones shoppers can see; if the store later hides the shelf an item is on, the editor
  says "(hidden by the store)" and you can move it.
- **Options & variants** — the choices a buyer makes, and a row for each combination.
- **Pictures** — up to twelve.
- **Parts & cost**, **Files**, **FAQ**, **Versions** and **Page** — each has its own section below.
- **History** — every change to the item, yours and the store's, newest first.
- **Preview** — the product page as a shopper will see it, including changes you haven't saved.

Changes to an item that's on sale show in the store right away.

If somebody at the store saved the item after you opened it, your save is refused with a message
saying so, so you never overwrite their changes without knowing. Press **Reload**, then make your
change again.

![An item's editor: its details, and the tabs for everything else](/help/media/selling-in-the-store/item-edit.png)

## Options and variants

Options are what a buyer chooses — up to three, like Color and Size. Add them, press **Save
options**, then **Generate variants** to make a row for each combination.

**A new variant starts switched off, with no price.** The store sets each variant's price, and
until it does, the variant can't be switched on. You can change a variant's SKU, switch a priced
one on or off, and choose which one is shown first. You can't generate variants for an item that's
on sale, because the new ones wouldn't have prices — take it off sale first.

**Stock** is yours to keep up to date. Use the box on each row to adjust it for stock received, a
correction after a count, or damaged items. Every change is recorded, with a reason.

## Pictures

Add up to twelve. The first one is shown on cards and in the cart; move pictures earlier or later
to change the order. You can tie a picture to one variant, so choosing that color shows that
photo. The camera details and location are removed from every picture.

## Parts and cost

The **Parts & cost** tab lists what one unit is built from. For each part, give its price — either for a
whole pack (a bag of 100 resistors for $6.99) or for one piece — and how many pieces one unit uses
(0.5 for half a sheet). You can also add where to buy it, a datasheet link, a picture, and how
many you have on hand.

**Other costs a unit** covers things that aren't worth listing part by part: solder, glue, the box it ships
in.

The total — **A unit costs to make** — is the item's cost basis. What you're paid for each one sold
is its cost basis **plus** the asking price the store approved. Prices are kept to a hundredth of a
cent and only the total is rounded, so small rounding errors don't add up over a long list. If you've counted the parts on
hand, the tab also says how many units you could build from them.

Only you and the store see this tab; shoppers never do.

![Parts & cost: each part, what a unit uses, and what a unit costs to make](/help/media/selling-in-the-store/parts.png)

## Files

The **Files** tab holds an item's manuals, firmware, software and documents. For each file, choose
who gets it:

- **For buyers** — everybody who buys the item finds it under **Downloads** on their order page,
  once the order is paid. It stays there for as long as they keep the order, unless that item was
  refunded in full.
- **Private** — only you and the store. Use it for build notes, supplier sheets or the master copy
  of a manual.

**Upload a file** takes anything up to 95 MB: a PDF, a firmware image, a zip. Give it a title — it's
what buyers see — and, if you like, a version. **Write a manual** lets you write one on the site
instead; it reads well on a phone and prints cleanly. Type it with simple Markdown (`#` for a heading, `**bold**`,
`-` for a list), or **Import from a file** to start from a `.md` or `.txt` file you already have.
A Word document is best uploaded as a file.

**Edit** changes a file's title, kind, version or who gets it; **Remove** deletes it, and buyers who
had it lose it too. Every change is in the item's **History**.

![Files: a manual for buyers and private build notes, with upload and write-a-manual below](/help/media/selling-in-the-store/files.png)

## FAQ and questions

Shoppers can ask about your item from its page. A question comes to you privately: you get a
message, and the question waits under **Selling → Questions**. The store passes questions and
answers along, so you're never told who asked, and they aren't told who answered.

- **Answer…** sends your answer to the person who asked, and only to them.
- **Decline…** says you can't answer it, with a note if you like.
- **Add to the FAQ…** copies a good answer into the item's **FAQ**, where you can reword it for
  everybody if needed. The answer the shopper received stays as it was, and nobody is named.

The item's **FAQ** tab holds its common questions and answers, so you only have to answer them once.
They show on the item's page after the reviews, in the order you set. Switch **Show the FAQ** off to
hide them for a while without losing them.

![Questions: what shoppers asked, with Answer, Decline and Add to the FAQ](/help/media/selling-in-the-store/questions.png)

![The FAQ tab: each question and answer in order, and the switch that shows them](/help/media/selling-in-the-store/faq.png)

## Versions

Made a better one? On an item that has been on sale, open **Versions** and **Start a new version…**.
Name it (“v2”, “2026 edition”) and choose what happens to the old one when the new one goes on sale:

- **sells what's left, then comes off sale** — it stays on sale until its stock runs out;
- **stays on sale beside it** — both are sold, each page linking to the other;
- **comes off sale straight away**.

The new version starts as a hidden draft with everything from the old one — text, pictures,
options, variants, parts, FAQ and files — but no stock. Change what's new, add its stock, and ask for
it to go on sale as usual. Nothing happens to the old version until the new one goes on sale, and
you can change your choice until then. After that, it can't be changed. An old version that comes off sale keeps its page, which
says it's no longer made and links to the new one.

![Versions: this version's name, and Start a new version](/help/media/selling-in-the-store/versions.png)

## The item's page

The **Page** tab holds what the item's page says beyond its description:

- **Returns** — anything particular to this item, shown under the store's returns window
  ("Unused and in its box, please").
- **Warranty** — shown in its own section when you give one.
- **Videos** — up to three, mp4, webm or mov, 95 MB each. They play in the gallery after the
  pictures. The location and camera details are removed from the copy shoppers get.

The store decides whether an item accepts reviews; the tab tells you whether yours does.

![The item's page: returns and warranty words, and videos](/help/media/selling-in-the-store/page.png)

## Sending packages

**Each seller sends their own package.** When a paid order has your items in it you get an email and
a message, and it appears under **Selling → My Packages** with what goes in it and the buyer's name
and address (shown until it's delivered).

1. Buy the label the way you usually do — Pirate Ship, the post office, your own account.
2. Optionally **Mark packed** while you get it ready.
3. **Ship…** — choose the carrier and type the tracking number, or check **No tracking provided**.
   The buyer is emailed right away, about your package only.
4. **Mark delivered** when it arrives, if the store hasn't already.

You're credited the store's flat shipping rate for every package you send — even when the buyer's
shipping was free. If an order is on hold while the store reviews it, the package says so; wait
until the hold is lifted.

When an order has packages from more than one seller, the buyer sees each package with its own
tracking, and the order reads **Partially shipped** until every package has shipped.

![My Packages: what goes in each, where it goes, and Ship](/help/media/selling-in-the-store/packages.png)

## Your earnings

**Selling → My Earnings** shows what you've earned and been paid. You earn when a package **ships**.
For each unit, you earn its cost to make (from your parts list) plus the asking price the store
approved, both fixed when the order was placed. You also earn the store's shipping rate for the
label.

- **Owed to you** — everything not yet paid.
- **Ready to pay** — earnings older than the returns window, which the store pays.
- **Inside the returns window** — still recent enough for a return.
- **Paid to date** — every payment the store has recorded.

If a unit is returned after your package shipped, what you earned for it is taken back. The store pays you outside the
site — a transfer or a check — and records it here. The tables below break it down by item, by
order and by year: the year's **Paid** column is what goes on that year's tax forms.

![My Earnings: owed, ready to pay, and paid — by item, by order, and by year](/help/media/selling-in-the-store/earnings.png)

## Going on sale

When an item is ready, press **Ask to put it on sale…** and say what you'd like to be paid for each
one sold — on top of what its parts cost. Add a note for the store if it helps.

The store then sets the price shoppers pay, and either puts the item on sale — you get a message
— or tells you what to change first. You can withdraw a request while it's waiting.

You can **take an item off sale** at any time; the store is notified. To put it back on sale, ask
again. A **draft that has never been on sale** can be deleted. Anything that has been on sale can't
be deleted, because orders and history refer to it.

