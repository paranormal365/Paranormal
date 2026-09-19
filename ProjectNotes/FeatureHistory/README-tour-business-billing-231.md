# Item 231: one flat price for tour and event businesses

Branched thinking, same day as items 228–230 (2026-09-10). Ben: *"Update the tour business offer
pdf. Include our Logo in the design. Verify it is still all true and update it with any
improvements. Determine if we can create the tour group like we mention in the document. And
bill monthly and yearly specifically for a tour group."*

## What was true, and what was not

Every feature the mailing names was checked against the site and the app as they are today:

| Claim | Found |
| --- | --- |
| Free app with the Field Kit: magnetometer, sound meter, recorder stamping time, place and room | True |
| Guests submit photos and recordings; nothing public until the business accepts | True (item 111's evidence queue) |
| Events with RSVP on the site and in the app; a reminder the day before | True (`EventReminderJob`, 24 hours) |
| Listed as a ghost walking tour, finder filter, "what's near you" | True (item: walking tours) |
| Promoted card in the finder **and on the home page** | True — `PromotedGroupsCard` is on both |
| A publication of their own | True; publications are open to every kind of group |
| Walk-up guests signed up by a guide; capacity caps | True (item 199) — now said in the mailing |
| **$29 / month or $290 / year, one flat price, bring every guide** | **False until today.** The resolver priced every organization by its member count; a tour with eight guides would have landed in a $60 band |
| First three months free | True while the `FOUNDING` coupon is live (Sept 1 – Nov 30, 25 redemptions) — the footer now says so |

## What changed

- `SubscriptionTierResolver.Resolve(tiers, members, kind)`: a **business kind** (ghost walking
  tour, public event provider) is sold the active flat tier — the one not banded by members,
  lowest sort order — when one is on offer, and the member ladder otherwise. Every caller passes
  the organization's kind: checkout, quote, the admin subscription screens, the limit guard and
  the renewal job. The ladder's own validation ignores flat tiers, as it already did.
- No schema change. The flat tier is data: **Administration → Subscription Tiers**, a tier with
  "banded by members" unticked and two prices, 29 monthly and 290 yearly. Created on the testing
  copy as **Tour & Event Business**; production needs the same row entered there.
- The mailing (`docs/tour-business-pitch.html` → `IsHaunted-Tour-Business-Offer.pdf`) carries
  the logo, the corrected and added claims, the coupon's limit, and a line saying a business is
  never priced by the size of its team.
- Help: site-administration's Billing section explains the flat price and how to offer it.

## Verified

| Check | Result |
| --- | --- |
| Resolver tests | 4 new cases: business kinds get the flat tier, groups keep their band, no flat tier falls back, retired or unpriced flat tiers are not on offer |
| Playwright `TourBilling` | A ghost walking tour registered through the wizard's own endpoint by an ordinary member is quoted "Tour & Event Business — Monthly, $29.00" on its billing page; deleted afterwards |
| Playwright `StartGroupWizard` | The wizard still founds a group |
| Full suite | see the commit |

## Can the tour group be created as the mailing says?

Yes. Sign in, **Start a Group**, choose *ghost walking tour*: the page is public from the first
minute (address shown, events public by default), it carries the tour badge, the finder's
walking-tours filter finds it, and its billing page now quotes the flat price. The mailing's
"reply to this email and we set it up" remains true as a service, and the self-service path is
now stated beside it.
