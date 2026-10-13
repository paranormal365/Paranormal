# Help to restore when iOS 1.1.3 is released

`ios-1.1.3` was merged into master on 10/08/2026 so the app's code and the violet icon live with
everything else, but 1.1.3 is not in the App Store yet. This paragraph was taken back out of
`Ben.Web.Services/Help/Content/the-mobile-apps.md` (after the app guide link) so the help never
describes a screen nobody can open. Put it back in the same place when 1.1.3 is live:

> **Is mine the newest?** Profile shows which version you have, under **App**, and checks whether a
> newer one is in the App Store each time you open it. When there is, it says what changed and
> **Update in the App Store** takes you straight there. **Check for updates** asks again. If the new
> version needs a newer iOS than your phone has, it says so, so you know to update iOS first.

Also on release: the home page's app icon (`Ben.Web.Website/wwwroot/static/images/app/app-icon.png`),
the QR code's centre and the app guide still show the green icon; swap them for the violet one.

## Designed menus (10/13/2026)

1.1.3 also draws event menus in their own designs. Until it is live, the help still describes the
1.1.2 menus. When 1.1.3 is released:

1. Copy `Ben.iOS/help-1.1.3/iphone-event-menus.png` over
   `Ben.Web.Website/wwwroot/help/media/the-mobile-apps/iphone-event-menus.png`, then delete the folder.
2. In `the-mobile-apps.md`, under **During the event**:
   - "or the venue hasn't shared menus with you, they simply aren't listed" becomes
     "or no menus are published, they simply aren't listed".
   - The **Menus** bullet becomes:

     > - **Menus** — each menu looks the way the hosts designed it, just as on the website: when it's
     >   served, its price, what the event covers, and the marks on each dish. **PDF** opens the same PDF
     >   the website gives, to save or share. Anyone can see published menus, without signing in: they're
     >   also on the event's own page in the app, under **See the menus**, with what the event's price
     >   includes.

   - The picture's caption "A menu, course by course" becomes "A menu, drawn in the look the hosts chose".
3. In `going-to-an-event.md`, after "**Download this menu as a PDF** saves a copy to print or keep.", add:
   "The iPhone and iPad app shows the same menus, in the same look, with the same PDF."
4. Rebuild the product documentation PDF.
