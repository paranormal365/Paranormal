# Test data written while building item 233's composer tools (2026-09-11)

**Database: `IsHauntedDb_player` only** — the testing copy. Nothing in this document was written to
`IsHauntedDb`, which is production. Every row below came from a Playwright test, a help-screenshot
capture, or a by-hand check on the running dev hosts.

This exists so the testing copy can be put back the way it was. Counts and ids were read from the
database itself, not from memory.

---

## 1. Schema

Twelve migrations are applied to `IsHauntedDb_player` that are **not** on production. The item-233
ones, in order:

```
20260910185513_Tours
20260910193424_TourDetailsAndGuides
20260910194228_TourReviews
20260910201132_TourGallery
20260910212232_MediaRetention
20260910213252_TourAuditFixes
20260911004050_TourSocialLinks
20260911010757_EventTimeZone
20260911113836_ReportACase
20260911122732_PollsSchedulingAndPlace
```

Backing schema out is `dotnet ef database update <the migration before Tours>`, which drops the tour
and poll tables. It is not part of the row clean-up below and should only be done if the whole
branch is abandoned.

---

## 2. Rows created

| Table | Rows | Where they came from |
|---|---|---|
| `OrgMessages` (PublicFeed) | 24 | Playwright feed tests and the help captures |
| `OrgMessages` (PublicCaseComment) | 1 | checking comments on a published case |
| `OrgMessages` (OrgBroadcast) | 2 | **not mine** — the parallel scoped-CSS session |
| `MessagePolls` / `MessagePollOptions` / `MessagePollVotes` | 6 / 12 / 6 | every poll on the site is test data; the feature shipped today |
| `OrgMessageReports` | 16 | the captures clear the feed by reporting and hiding, which is the only route there is |
| `Tours` | 8 | tour-tier verification over two days |
| `TourGuides` / `TourGalleryImages` / `TourReviews` / `TourSocialLinks` | 1 / 2 / 1 / 5 | same |
| `OrgCalendarEvents` with a tour | 2 | "Saturday walk" ×2 |

### Three posts are scheduled and will appear on their own

These are **not yet public** and go up two days after they were written unless they are removed:

```
014d9d5a-d7d5-40b7-80df-826c9292ed86   s59942f43fa2 going up later
17223cfb-b6ca-42d2-8bed-b86c34cc313c   s8af6426f073 going up later
7ef65df7-ac44-4f9c-a0e6-57414179d36c   s644ed826d93 going up later
```

They are the reason this document exists. Everything else sits still; these have a clock on them.

### Visible feed posts left behind

```
1611ebdc-dcb4-4559-b470-66c0237e0537   Planning next week's session — settle it for me.   (poll)
7ae58356-bc61-4d1e-bafd-093f15d2daef   Last month's write-up …                            (link card)
44a5e9fe-13ef-4ac4-8c5d-0fa214769db7   p0b7754aee81 which way?                            (poll)
409da1bb-6abb-4cca-9ad8-875962a8e890   c5d5676b8297 second thoughts
4a5966a8-b861-4de3-af9d-76a2ecd8fbbb   sbebbfe76d6c going up later
09582e79-1b4e-4440-a72a-8aa2292dc78d   pcf0becb5c27 which way?                            (poll)
28655bba-be85-4e6f-8598-abb0d91f0907   l5869d97abef cold down here                        (tagged place)
42a16aef-ea8c-499e-bfd9-fe85dc753fc3   Line one of three …                                (NOT mine — the CSS session)
```

The rest of the day's posts are already hidden, by the capture's own clear-the-feed step.

---

## 3. Rows changed rather than created

1. **Three posts that predate today were hidden**, by the captures clearing the feed. They are
   seeded fixtures, so hiding them is a change worth undoing:

   ```
   e8a7df35-4eb8-4bcb-86c0-af8d6d33189a   Playback check — seeded (3)
   6d298985-4796-4c41-9686-bfeda0eb4523   e2e post — seeded for the test-posts page (2)
   0ff21cdd-64ee-4730-af68-ba178e316898   e2e post — seeded for the test-posts page (1)
   ```

2. **`james.thornton@benco.dev`'s password was reset** to the value in
   `Ben.Data.WebApi/appsettings.Development.json`, and its lockout cleared. The row had drifted from
   the configuration and every test signing in as the member seat was failing on the login page. Done
   through the seeder's new `SeedData:SeedOrganization:ResetPasswords` flag, which is off by default.
   Nothing to back out — this put the account back to what the configuration already said it was.

3. **One case vote was deleted.** `sarah.mitchell@benco.dev`'s vote on case **2026-002** was removed
   so the vote button's picker would open for a screenshot. It was cast before today and its value
   was not recorded first. If it matters, she can vote again; the case's other 16 votes are intact.

---

## 4. Backing the rows out

Run against **`IsHauntedDb_player`**. Read the `USE` line before running it.

```sql
USE IsHauntedDb_player;
GO

BEGIN TRANSACTION;

-- Everything this session's testing wrote to the feed, by id. Listed rather than matched on a
-- date, so a row somebody else wrote today is not swept up with them.
DECLARE @Mine TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @Mine (Id) VALUES
  ('014d9d5a-d7d5-40b7-80df-826c9292ed86'),  -- scheduled, still waiting
  ('17223cfb-b6ca-42d2-8bed-b86c34cc313c'),  -- scheduled, still waiting
  ('7ef65df7-ac44-4f9c-a0e6-57414179d36c'),  -- scheduled, still waiting
  ('1611ebdc-dcb4-4559-b470-66c0237e0537'),
  ('7ae58356-bc61-4d1e-bafd-093f15d2daef'),
  ('44a5e9fe-13ef-4ac4-8c5d-0fa214769db7'),
  ('409da1bb-6abb-4cca-9ad8-875962a8e890'),
  ('4a5966a8-b861-4de3-af9d-76a2ecd8fbbb'),
  ('09582e79-1b4e-4440-a72a-8aa2292dc78d'),
  ('28655bba-be85-4e6f-8598-abb0d91f0907'),
  ('02a7206a-23ad-49c9-921d-f7fb266911af'); -- the case comment

-- Every feed post written today that is already hidden, plus the ones above. The hidden ones were
-- written by the same runs; they are simply the ones the capture cleared away afterwards.
INSERT INTO @Mine (Id)
SELECT m.Id FROM OrgMessages m
WHERE m.DateCreated >= '2026-09-11'
  AND m.ChannelType = 3                    -- PublicFeed (see OrgMessageChannel)
  AND m.HiddenUtc IS NOT NULL
  AND m.Id NOT IN (SELECT Id FROM @Mine);

-- Children first: the report FK is NoAction on purpose, so a report outlives what it is about
-- unless it is removed deliberately.
DELETE FROM OrgMessageReports  WHERE OrgMessageId IN (SELECT Id FROM @Mine);
DELETE FROM MessagePollVotes   WHERE MessagePollId IN (SELECT Id FROM MessagePolls WHERE OrgMessageId IN (SELECT Id FROM @Mine));
DELETE FROM MessagePollOptions WHERE MessagePollId IN (SELECT Id FROM MessagePolls WHERE OrgMessageId IN (SELECT Id FROM @Mine));
DELETE FROM MessagePolls       WHERE OrgMessageId IN (SELECT Id FROM @Mine);
DELETE FROM OrgMessageHashtags WHERE OrgMessageId IN (SELECT Id FROM @Mine);
DELETE FROM OrgMessageMentions WHERE OrgMessageId IN (SELECT Id FROM @Mine);
DELETE FROM OrgMessageLikes    WHERE OrgMessageId IN (SELECT Id FROM @Mine);
DELETE FROM OrgMessages        WHERE Id IN (SELECT Id FROM @Mine);

-- Put back the three seeded posts the captures hid.
UPDATE OrgMessages
   SET HiddenUtc = NULL, HiddenByAppUserId = NULL
 WHERE Id IN ('e8a7df35-4eb8-4bcb-86c0-af8d6d33189a',
              '6d298985-4796-4c41-9686-bfeda0eb4523',
              '0ff21cdd-64ee-4730-af68-ba178e316898');

-- COMMIT TRANSACTION;   -- uncomment once the row counts above look right
ROLLBACK TRANSACTION;
```

The two `OrgBroadcast` rows at 13:21 and the post beginning "Line one of three" belong to the
parallel session working on the unreferenced stylesheet, and are deliberately not in the list.

### The tours are a separate decision

The eight `Tours` rows and their dates, guides, gallery images, review and social links are what the
tour tier was verified against, and several of the item-233 screenshots in the help documents are
pictures of them. Removing them would make those pictures show something that no longer exists.
Leave them unless the testing copy is being reset wholesale.
