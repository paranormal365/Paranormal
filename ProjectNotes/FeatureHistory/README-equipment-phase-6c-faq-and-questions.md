# Equipment Phase 6c — Owner FAQs and Anonymous Questions

Branch: `equipment-phase-6c-faq-and-questions` · Backlog item **#55**, third of four Phase-6
branches. Stacked on 6a and 6b, both still unmerged — merge in order.

Migration: `AddEquipmentFaqAndQuestions` (applied to dev SQL; `scripts/create-database.sql`
regenerated).

## Why

Ben asked for two things that turned out to be one. Borrowers should be able to **ask the owner a
question** before committing, and owners should be able to write a **general FAQ** that gets
gathered on the make and model page. The second is what the first produces once a good answer has
been given twice.

His constraint was specific and it shapes everything below: *"FAQs on private items and questions
for people loaning are internally sent and the name of the people borrower and loaner are not
visible to one another."*

## Anonymous both ways, and why that is the point

A question is how you find out whether a thing has a quirk you need to know about. People do not
ask that of someone whose answer might decide whether they get lent the gear — so a named channel
would mostly go unused, and the questions worth asking are exactly the ones that would go unasked.

Both ends are hidden. The asker does not learn who owns the piece; the owner does not learn who is
asking.

**The anonymity is confined to this channel and the FAQ it feeds.** Loans keep names on both sides —
you should know who is holding your recorder — and a group's shared-gear list still names owners.
The help doc says this in both directions, at the point of asking as well as in the reference.

### Two independent mechanisms, because one is not enough

1. **Shape.** `ReceivedQuestionRecord` — what the answerer gets — has no asker id and no asker name
   to fill in. Not the same type with nulls: a null is a slot somebody later fills. A reflection
   test asserts the absence, mirrored for the other direction (`AskedQuestionRecord` cannot carry
   who answered).
2. **The notice.** Its body carries no name, and it is sent with `HideSenderIdentity` so the inbox
   projection will not name the sender either. Phase 6a found the inbox naming **every** sender,
   falling back to their *email address* — which would have defeated this entirely through a
   surface that has nothing to do with equipment.

Both tests were run against deliberately broken code before being relied on: interpolating the
asker's display name into the body, and flipping `HideSenderIdentity` to false. Each fails them.

The true sender is still stored on the row. Abuse has to remain traceable; anonymity is a property
of the projection, not of the record.

## The FAQ

Owner-authored, per item, **unattributed everywhere** — including on the owner's own item page,
where the reader already knows whose gear it is. The same shape feeds the make/model aggregate where
several owners' entries sit side by side, and one unattributed shape is safer than two that could
drift.

**The model-page aggregate draws from publicly-listed items only, for every caller alike** — not
per viewer. A member seeing an extra entry appear would learn, by inference, that somebody in one of
their groups owns this model. That is something no individual entry says, and the aggregate would be
saying it by its length. The test asserts this for an anonymous visitor, a stranger, a fellow group
member **and the owner of the private copy**; removing the filter fails it.

## Promoting an answer

Publishing **copies** the text into a new FAQ row rather than exposing the thread, and the text is
editable first — what reads well as a reply to one person rarely reads well as a public answer. The
question keeps what was actually said (`"does it take AAs lol"`), the FAQ gets the tidied version.

A stamp on the question records that it happened, in the same save as the FAQ row, and refuses a
second publish. Only an answered question can be published; declining closes it without one.

## Structural change worth noting

`ResolveItemAudienceAsync` moved out of `EquipmentItemDetailController` into `EquipmentAccess`. The
item page, its FAQ and its question channel now all ask the same method. Three near-identical
visibility predicates would eventually disagree, and the one that disagreed generously would be the
leak.

## Endpoints

| Route | Who |
|---|---|
| `GET api/equipment/items/{id}/faqs` | anyone who may see the item (anonymous for public ones) |
| `POST/PUT/DELETE …/faqs[/{faqId}]` | custodians — **404**, not 403, for everyone else |
| `POST api/equipment/items/{id}/questions` | anyone signed in who is not a custodian; 409 on own gear, 404 on retired |
| `GET api/me/equipment-questions/asked` | the asker's own list |
| `GET api/me/equipment-questions/received` | gear you look after; the shape cannot name askers |
| `PUT api/me/equipment-questions/{id}/answer` | custodian, `Open` only; `Decline` needs no text |
| `POST api/me/equipment-questions/{id}/promote-to-faq` | custodian, `Answered` only, once |

## UI

- **Things worth knowing** on the item page, inline-editable for custodians — a page, not a window,
  per the standing rule.
- **Ask a question** opens a small single-purpose window (which is what the rule *does* leave as a
  window), with the anonymity promise stated where the decision to ask is made rather than only in
  the help doc.
- **Gear Questions** (`/my-equipment/questions`), a new page in the main menu: Waiting-on-you and
  You-asked tabs, inline answer/decline/publish.
- The model page gains a **Things owners say about it** section.

## Verification

Full solution build, **0 warnings, 0 errors**. Full suite: **4,353 passing, 0 failing**
(Web 2,075 · Video 1,787 · Repository 306 · Sidecar 185) — 19 new.

**Still to do by hand** (Ben, two accounts):
- Ask from account B; confirm account A's notice and the notifications list and dialog show
  "Anonymous" and no name anywhere.
- Answer; confirm B's notice likewise names nobody.
- Publish; confirm the entry appears on the model page unattributed.
- Confirm a private item's FAQ never reaches the model page.

## Next

6d — mutual loan feedback and ratings, the last Phase-6 branch.
