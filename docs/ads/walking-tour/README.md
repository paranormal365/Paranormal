# The walking tour ad (35.2 s, with music and sound)

Ben's "Walking Tour Ad.mp4" (24.2 s, no sound) runs as it is until the robot's green hologram (~22.5 s),
which becomes a walking tour check-in in the site's colors (`hologram.py`; the green light it throws on the
robot turns cyan with it). A short push in, then a flying saucer like the city's crosses right to left and
fills the frame, painting the site's dark background behind it with sparkles in #7C5CFF / #22D3EE / #A78BFA.
The metallic logo (Ben's image) is built piece by piece (`pieces.py`), then the wordmark and the tagline.

    ~/.cache/ishaunted-ad/bin/python render.py "<Walking Tour Ad.mp4>" build/IsHaunted-Walking-Tour-Ad.mp4

`build/logo-metal.webp` is the metallic logo; neither it nor the source video is in git.

Music and sound: `score.py`, all real recordings (strings and harp from ~/Music/IsHaunted Strings; drums,
guitar, synths and every effect from the Logic Pro / GarageBand and Final Cut Pro libraries installed on this
Mac, decoded into build/snd on first run). One theme in E major: strings in 6/8 for the October evening; the
same theme in straight eighths at 145 bpm with real drums and grunge guitar chugs for the race (18 beats, the
stop landing as the camera slows); synths on the theme's chords for the robots; the theme note by note as the
logo builds, a landing chord on the wordmark and the cadence as a wink under the tagline.

    ~/.cache/ishaunted-ad/bin/python score.py build/walking-tour-score.wav
    ffmpeg -i build/IsHaunted-Walking-Tour-Ad.mp4 -i build/walking-tour-score.wav -map 0:v -map 1:a -c:v copy \
        -c:a aac -b:a 256k -shortest build/IsHaunted-Walking-Tour-Ad-sound.mp4
