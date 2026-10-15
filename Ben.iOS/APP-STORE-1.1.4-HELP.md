# Help to restore when iOS 1.1.4 is released

The session review changed on the phone on 10/14/2026, along with the website's player:

- tapping a photo goes to the moment it was taken;
- the mark being passed lights up;
- the map follows the marker, with Recenter;
- the arrow turns smoothly and is drawn relative to the map;
- Play where nothing was recorded starts at the next recording.

None of this is in the App Store yet, so `Ben.Web.Services/Help/Content/the-mobile-apps.md` still describes the 1.1.3 review. When 1.1.4 is live, edit **Reviewing what you recorded** as follows.

1. The first paragraph becomes:

   > A finished session plays back on the phone: one playhead moves through the magnetic trace, the map,
   > the compass and whatever was being recorded at that second, with every mark listed so you can jump
   > to it. Jumping to a mark takes you a few seconds *before* the event, so you hear what led up to it,
   > and the mark playback is passing lights up in the list. Press Play where nothing was recorded and
   > playback starts at the next recording.

2. In the photo paragraph:

   > **Tap a thumbnail** to pause and see the photo full size; tap it again to put it back in the strip,
   > and playback continues where it left off.

   becomes:

   > **Tap a thumbnail** to go to the moment it was taken and see the photo full size; tap it again to
   > put it back in the strip, and playback carries on from there.

3. After the photo paragraph, add:

   > On the map, the arrow turns smoothly to show which way the phone was facing, and the map keeps the
   > marker in view as it moves. If you move or zoom the map yourself, it stays where you put it until
   > you tap **Recenter**.

The apps changelog (`Ben.Web.Services/Changelog/Content/ios.md`) holds these under `## Unreleased`.
When 1.1.4 goes to App Review, that heading becomes `## 1.1.4 · yyyy-MM-dd` in the same commit that sets
`MARKETING_VERSION` to 1.1.4; a test fails if the two disagree.

## The Paranormal Platform name (10/14/2026)

1.1.4 also renames Field Kit to the Paranormal Platform in the app, with the tab reading
**Paranormal**. The website and its other help pages use the new name already; `the-mobile-apps.md`
walks through the app's own screens, so it keeps saying Field Kit until 1.1.4 is in the App Store.
When it is:

1. Apply the prepared rename of that page, from the repository root:

   ```bash
   git apply Ben.iOS/APP-STORE-1.1.4-the-mobile-apps-rename.patch
   ```

   If the page has changed since and the patch no longer applies, the patch is still the list of
   every line to change.
2. Recapture the iPhone and iPad screenshots in that page (`HelpMediaCaptureTests`, and the captures
   named in `ProjectNotes/FeatureHistory` for the launch and meter shots): every one that shows the
   tab bar or the Field Kit screen shows the old name.
3. Rebuild the product PDF and the app guides.

## Added 10/15/2026: iPhone Duo, and details added to marks

Also held back until 1.1.4 is live. In `the-mobile-apps.md`:

1. At the end of **What's in the app today**, add:

   > **On iPhone Duo**, the app is a phone when folded. Open it and the inner screen gets the room it
   > deserves: the menu becomes a **☰** button at the top of the right-hand column, and while a session
   > runs, the rest of that column holds the investigation's own controls, top to bottom: **mark** the
   > moment, **log what happened** (a cold spot, a touch, a voice, a sound, something seen or smelled,
   > or feeling uneasy), **ask a question and listen**, a **note**, **change room**, **reset the base**,
   > the **flashlight**, and the levels. **Stop** stays on its own at the bottom, away from the rest.
   > Fold or unfold whenever you like: the session keeps recording.

2. In **EVP question and answer**, add:

   > On iPhone Duo, the microphone in the side column does the same in fewer steps: press it while you
   > ask, and the phone writes down what you say if typing is on (in the session's levels; it happens on
   > the phone, and nothing is sent). Press it again to stop writing and start listening, and a strip
   > across the top counts how long the room has had to answer. Press it again for the next question,
   > or **Done listening** when you've finished.

3. In **Reviewing what you recorded**, after the marks paragraph, add:

   > **Adding details to a mark.** In the dark you tap "Cold spot" and keep going; afterwards you can say
   > more. Until the session is sent, each mark in the review has **Add details**: write or say what
   > happened, who else noticed, how long it lasted. What was recorded at the moment never changes: the
   > time, the kind of mark and anything said then. Your details are kept beside it, marked as added
   > after the session, and go with the session when you send it. Once it's sent, it can't be changed;
   > the website shows the details under the mark and doesn't edit them. Only text can be added: no
   > new marks, photos or recordings.
