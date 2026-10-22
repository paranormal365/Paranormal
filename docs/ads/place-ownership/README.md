# The place ownership ad (72.7 s, with music and sound)

Ben's "Advertisement Place Ownership.mp4" (64.8 s, ten scenes, made from the prompts in
"Advertisement - Public Location.md"): people across history and beyond claim famous public places on
their devices. Every device's screen now shows the site's venue claim for that scene's place, the
running man's name tag reads "John Hancock", each scene has the same tune in the instruments of its own
time and place, the silent scenes have sound, and it ends on the new icon.

| # | Scene | Screen shows | Music |
|---|---|---|---|
| 1 | The final, the goalie (0–12.4) | Championship Final Stadium, claimed by Keepers of the Keep | brass anthem, drums, the save |
| 2 | Michelangelo, the Sistine Chapel (12.4–18.3) | The Sistine Chapel, Buonarroti Paranormal | choir, lute, recorder in the chapel's echo |
| 3 | The farmer and the rubber-band ball (18.3–24.0) | World's Largest Ball of Rubber Bands, Overall Investigators | bluegrass: fiddle, banjo, guitar |
| 4 | Bartholdi, the Statue of Liberty, 1886 (24.0–29.7) | The Statue of Liberty, Société Bartholdi | French musette waltz: accordion, tuba, clarinet |
| 5 | The astronaut and the Great Wall (29.7–35.9) | The Great Wall of China, Low Earth Orbit Paranormal | celesta and synth pads; erhu and guzheng for the Wall |
| 6 | John Hancock, 1776 (35.9–41.8) | Independence Hall, The Hancock Society | fife and drum |
| 7 | The Mars rover (41.8–47.5) | Jezero Crater, Rover Paranormal Unit | analog synths |
| 8 | The Pharaoh at the Sphinx (47.5–53.2) | The Great Sphinx of Giza, Pharaoh Tours | oud in the Hijaz mode, darbuka, ney |
| 9 | The prairie dog (53.2–59.0) | Prairie Dog Town, Burrow Busters | cowboy trot: harmonica, guitar, hoofbeats, dobro |
| 10 | The operating room (59.0–64.8) | "Congratulations! You own Operating Room 3 now." | piano and strings |
| E | The ending (64.8–72.7) | the icon built piece by piece, the wordmark, "Claim it before the ghosts do." | the tune passed between all of the above, then all together |

## How it is made

    ~/.cache/ishaunted-ad/bin/python screens.py     # the claim pages, rendered by Chromium in the site's look
    ~/.cache/ishaunted-ad/bin/python pieces.py      # the new icon's pieces and the wordmark
    ~/.cache/ishaunted-ad/bin/python track.py       # each screen's corners, every frame
    ~/.cache/ishaunted-ad/bin/python tag.py         # the name tag's corners
    ~/.cache/ishaunted-ad/bin/python render.py      # the picture: build/IsHaunted-Place-Ownership-Ad.mp4
    ~/.cache/ishaunted-ad/bin/python score.py       # the sound: build/place-ownership-score.wav
    cd build && ffmpeg -i IsHaunted-Place-Ownership-Ad.mp4 -i place-ownership-score.wav -map 0:v -map 1:a -c:v copy \
        -af "volume=5.3dB,alimiter=limit=0.7:attack=3:release=60:level=disabled,aresample=48000" \
        -c:a aac -b:a 256k -shortest -movflags +faststart IsHaunted-Place-Ownership-Ad-sound.mp4

The home page plays `placeownership-{hd,sd}.mp4` and `placeownership-poster.jpg` from
Ben.Web.Website/wwwroot/static/video/ads (HomeAdPlayer.AdIds). The film grain makes it heavy, so its web
copies sit a few steps lighter than the other ads' (still 0.98 SSIM to the master):

    M=build/IsHaunted-Place-Ownership-Ad-sound.mp4; O=../../../Ben.Web.Website/wwwroot/static/video/ads
    ffmpeg -i $M -c:v libx264 -preset slow -crf 26 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart $O/placeownership-hd.mp4
    ffmpeg -i $M -vf scale=960:540 -c:v libx264 -preset slow -crf 27 -pix_fmt yuv420p -c:a aac -b:a 112k -movflags +faststart $O/placeownership-sd.mp4
    ffmpeg -ss 71.8 -i $M -frames:v 1 -q:v 3 $O/placeownership-poster.jpg

The source video lives in ~/_Fun/IsHaunted (override with PO_SRC); nothing in build/ is in git. The
screens and pieces are rendered with the Chromium that the e2e project's Playwright installs (build
Ben.Web.Playwright once, or point PW_DRIVER at a Playwright driver).

- **Screens** (`track.py`, `comp.py`): the green is keyed, so fingers, gloves, a robot claw and paws stay
  in front of the page; its four corners are fitted each frame from the green's hull, kept in the
  device's own order as it turns (`ROT` says which corner is the page's top), filled across frames where
  a hand covers too much, and rebuilt from the device's shape where the frame's edge cuts it off. The
  green light the old screen threw on hands, fur and faces is turned to the page's violet. The 1776 iPad
  showed a page of text rather than green; it is keyed on its mint color, with only skin and the cuff in
  front, and only what reaches in from beyond the screen (a pale patch of the old page is not a hand).
  A tracked device whose green mostly falls outside its outline (the goalie's thrown tablet) is refitted
  from that frame's own green; the Sistine tablet rising into frame (`ENTER`) takes its shape from the
  first whole frame, set on the green's top edge, and while tossed onto the plank (`OWN`) each frame's
  outline is settled onto the bezel. The pages switch at the taps in the footage (`SCHEDULE`).
- **The name tag** (`tag.py`): the card is unwarped flat, "John Handook." is inpainted away, "John
  Hancock." is set in Palatino at the old line's place, width and ink, and the card is warped back —
  through the dissolve into Mars too.
- **The receiver's kit** (`kit.py`): the footage dressed the player the goalie watches (8.3–11 s) in red,
  though he plays in his teammate's blue. His shirt is told apart by color — a big, very saturated, true
  red, where the goalkeeper's shirt folds are red-orange and the banners paler — and his shorts and socks
  by sitting just below it; a motion-blurred frame borrows where his shirt was a moment ago. Those pixels
  turn blue with their own shading.
- **The surgery** (`render.py`): the camera eases in on the phone (×5.2) while the page is drawn sharp at
  the new size, then fades into the site's dark background.
- **The ending** (`render.py`, `pieces.py`): the icon's paths from wwwroot/static/images/is-haunted-mark.svg,
  grouped (arc, crescent, four windows, ghost, face), on the icon's violet tile; the wordmark in Irish
  Grover as the browser sets it (Cairo's own text drops the font's kerning).
- **EastWest instruments** (10/22/2026): 33 Opus instruments from Ben's ComposerCloud subscription (Hollywood
  Brass/Strings, Goliath, Ra, Silk, Hollywood Fantasy, Hollywood Orchestral Percussion) are rendered in Logic
  through Opus, one track each, every semitone C1–C7 held 3 s (`build/ew/notes-C1-C7.mid`), exported to
  `build/ew/Inst N.aif`. `instruments.EW_TRACKS` names which track is which; the Logic project is
  ~/Music/Logic/IsHaunted EW Note Sets.logicx. Opus will not load inside Python (its copy protection ends the
  process), which is why Logic renders them. Notes are played from their own key, never repitched; a note
  outside an instrument's range moves by octaves into it.
- **Music** (`score.py`, `instruments.py`): every note is a real recording — Logic's orchestral, guitar,
  piano, mallet, synth and Chinese string samples; banjo, harmonica, accordion, oud and dobro notes cut
  out of Apple Loops (kept only where two pitch methods agree, so a ringing neighbor string can't pass
  for the note); the VSCO strings in ~/Music/IsHaunted Strings; Final Cut Pro's effects for the
  space station, the hall in 1776 and the desert queue. Each scene's music sits 1.5 dB under its own
  sound, so the crowd still roars; the whole is at -14.7 LUFS like the other ads.
