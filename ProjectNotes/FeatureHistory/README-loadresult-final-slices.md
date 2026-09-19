# Item 120, final slice — the last seven areas, and the ban

Cms, Places, Media, Publications, Membership, Feed and Account together — **26 methods** — plus
**15 more inside `WebApiClient` itself** that the ratchet never counted. Ratchet **26 → 0**.

## The ratchet becomes a ban

There were 120 of these. A hard ban at the start would have been one unmergeable change touching
hundreds of call sites, so it was a count that could only fall, landing in seven slices. At zero the
scaffolding comes down: `SwallowedFailureRatchetTests` now forbids `?? []` anywhere in
`Ben.Web.Services/WebApi`, with **one** exclusion — `LoadResult.Items => _items ?? []`, which is the
mechanism that makes the rule enforceable rather than an instance of breaking it. Verified to
discriminate.

It also scans the **whole folder** now, not just `BenAdminClientAdapter.*.cs`. The user slice found
two methods whose swallows lived in `WebApiClient`; the old count was a floor, not a total.

## Three mutations wearing the same defect

`LoadResult` answers "is this list real?". These three ask "did this happen?", so they report an
error beside the list instead:

- **`SetMyEquipmentSharesAsync`** — a refused save became an empty share list, and the editor closed
  its dialog reporting success. Somebody believed their equipment was shared when nothing saved.
- **`SetInvestigationLeadAsync`** — returns the whole roster, so a refusal would have wiped everyone
  off the screen as though the change had worked.
- **`ScanAudioForEvpAsync`** — returns null rather than an empty list. An empty list does not mean
  the recording came back clean, it means the scan never ran, and on this site "no EVP detected" is
  a finding somebody acts on.

## What is deliberately not finished

**22 pages can now see a refusal and still render two states.** They are recorded in
`AwaitingRenderPass` — a debt list, not an exemption — with a second ratchet holding it at 22 so it
can only fall. Item 141.

They are **no worse than before this branch**: they used to receive a bare empty list and now
receive a result whose `.Items` is empty. The sentence on screen is unchanged. What is new is that
the truth is available at the call site, and the list says where it is going unused.

Splitting it this way was deliberate. Wiring 22 more render states in the same pass as a 41-method
mechanical conversion would have made a large diff whose testing story is "it compiled", which is
exactly how this class of change introduces worse bugs than it removes.
