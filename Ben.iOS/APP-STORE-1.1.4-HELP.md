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
