# The campfire ghost ad (47.7 s)

Kids at a campfire; one watches a bedroom video on a tablet (white screen, perspective-tracked and keyed under
his thumbs); an invisible ghost wrecks the room, bursts out of the tablet and rushes the lens; the flash becomes
a night bedroom it tears through and leaves by a dark door; that turns out to be playing in the IsHaunted video
editor (playhead scrubbed back, a "Ghost spotted" marker, a "GHOST?!" callout); the editor flies onto the kid's
tablet, the campfire rewinds to him, he melts into the ghost, which wipes to the site's dark background; the
logo builds, "Even ghosts need a good video editor." / "Upload it. Mark it. Cut it. Share it."

- `picture/`: the render scripts. Source clips (Campfire.mp4, Ghostee.mp4, Night_Ghost_No_Hunter.mp4) are not in
  git. Order: `track4.py` + `smooth4.py` (screen quads), `comp.py` (ghost video on the tablet), `ghost_exit2.py`
  (the ghost leaves the tablet), `assemble.py` (into the night clip), `editor_scene.py` (the editor, from the
  help screenshots in wwwroot/help/media/using-the-video-editor), `kidmask.py` + `seg.py` (the kid's matte,
  u2net_human_seg), `act2.py` (tablet, rewind, melt, rush, logo via `ending.py` and docs/ads/ishaunted-ad/common.py).
- `score_cg.py`: music and effects on the strings kit in ~/Music/IsHaunted Strings, cue by cue against the cut.
- Output: Ben.Web.Website/wwwroot/static/video/ads/campfire-{hd,sd}.mp4 and campfire-poster.jpg.
