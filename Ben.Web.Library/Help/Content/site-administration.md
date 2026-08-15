---
title: Site Administration
summary: Sitewide settings, the clipart library, and the audit log.
section: Site Administration
audience: AppAdministrator
order: 70
---

Visible to app administrators only.

## Site settings

**Administration → Site Settings** holds values that apply to the whole site. Nothing personal
belongs here — settings about one person live on their profile, settings about one group live in
that group's settings.

Adding a new setting is a code change, not a database one: settings are declared in
`SiteSettingKeys.Seed`, and the page renders whatever is declared.

## The clipart library

**Administration → Clipart Library** curates the shared artwork every group can use in the video
editor. Upload the file first, then publish it here by its file id. The format is read from the
file itself — SVG, PNG, WebP, AVIF, GIF or Lottie — and anything else is refused rather than
published as something the editor cannot draw.

Assets are **retired**, never deleted. Projects reference artwork by id, so removing one would
break renders that already use it. A retired asset leaves the catalog and stays downloadable.

## Audit log

**Administration → Audit Log** records every mutation with who made it. It is filtered and paged
on the server, so date and user filters apply to the whole history rather than the page on screen.
