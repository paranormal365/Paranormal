# Held run months on the billing card; archived findings; findings audit (10/25/2026)

Branch: `fix/held-months-and-archived-sessions` (from develop). Ben: "do 5 and 6", then "also do 7 and 8".

- **5, billing card:** `OrgEventCreditsView.RunMonths` (`EventCreditViews.BuildAsync`), shown in `EventCreditsCard`
  under "Extra months for a show's run": ready to use / on the show / expired / refunded. Before this, a month
  that came back from a called-off show could only be seen on a show's page.
- **6, archived events:** `EventFindings.MaySeeAsync` reads the hosted event's own state, not its calendar row,
  which archiving takes off the public site. Published findings stay open through archiving
  (`EventFindings.ShowsFindings`), sessions shown with the results too (`EventSessionPublication`), a called-off
  event's go to the people who were there, and a removed event's to nobody.
- **7, audit:** publish/unpublish, the place-page mode and each chosen piece go through `SaveAndAuditAsync`.
- **8:** memory index tidy (not in the repo).

Tests: `EventCreditEndpointTests.The_card_lists_extra_months_held_and_on_a_show`; `EventFindingsTests` archive,
called-off, removed and organizer-controller tests; `SessionsInTheResultsTests.Archiving_the_event_keeps_its_shown_sessions`.
Each new one checked against the code without its change. Help (organization-administration), change log, PDF.

Deploy: web API and website. No migration.
