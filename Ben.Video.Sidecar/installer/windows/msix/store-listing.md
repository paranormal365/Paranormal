# Store listing copy for IsHaunted.com SideCar

Written 2026-09-23. Every heading below is a field on the Partner Center **Store listings** page,
in the order they appear, so this can be pasted straight down the form. The character limit for
each field is in brackets; everything here is inside it.

Two of these are not just marketing and should not be reworded casually:

* **Additional license terms** carries the FFmpeg attribution. The package bundles an LGPL build of
  FFmpeg, and the LGPL requires that we say so, name the licence, and tell people where the source
  is. Removing that text would put the submission out of compliance.
* **Description** is written to answer the certification question a background helper invites -
  what does this do that is worth installing - in its first paragraph rather than its fourth. See
  the note at the bottom.

---

## Product name

    IsHaunted.com SideCar

Fixed - this is the reserved name, and the package manifest's DisplayName has to match it.

## Short description  [270, NOT 1,000]

    The field takes 270 characters, not the 1,000 guessed here first - Ben read the limit off the
    form, 2026-09-23. Paste as one paragraph. This is 256.

    Exports that used to crawl now fly. SideCar hands the work of the IsHaunted.com video editor - cutting, joining, encoding - to your own processor instead of a browser tab. Long clips finish fast, and your footage never leaves the computer it is already on.

    The privacy line is last on purpose: it is the point a buyer does not expect, and it lands
    better as the thing they are left holding than as an item in the middle of a list.

    An alternate at the same length, leading with an image rather than a claim, if the opening
    ever reads as too strong:

    A browser tab is a small room to edit video in. SideCar moves the heavy work - cutting, joining, encoding - onto your own processor, where there is room to swing. Exports finish in a fraction of the time, and your footage never leaves the machine it is on.

## Description  [10,000]

    Video editing in a browser has a ceiling. The browser has to do the work inside a tab, on one
    core, with whatever memory it is allowed, and your footage has to be handed to it. Long clips
    crawl. Large ones fail.

    SideCar removes that ceiling. It is a small program that runs quietly on your computer and does
    the heavy work for the IsHaunted.com video editor: splitting clips, joining them back together,
    building thumbnails, reading what a file actually contains, and encoding the finished video. It
    uses your processor rather than a browser's share of it, so exports finish in a fraction of the
    time, and long recordings stop being a problem.

    It also means your footage stays put. Rendering happens on the machine the files are already
    on. Nothing is uploaded in order to be rendered, and nothing is sent anywhere to be processed.

    HOW IT WORKS

    SideCar has no window of its own, by design. You turn it on from the video editor and it
    appears there as a switch, with the editor reporting whether it is running. When it is on, the
    editor hands it the work. When it is off, the editor falls back to rendering in the browser and
    everything still works - just slower. You can turn it off from the same switch at any time, and
    it will tell you if it is in the middle of a render rather than stopping on top of your export.

    It listens only on this computer, on a loopback address, and only answers the editor. It does
    not run at startup unless you tell Windows to let it, in Settings > Apps > Startup.

    WHAT IT NEEDS

    A free IsHaunted.com account and the video editor at https://ishaunted.com. SideCar is free.

    FORMATS

    Exports to H.264 (MP4) using Windows' own built-in encoder, and to VP9 (WebM). Reads
    the common camera, phone and screen-recording formats.

## What's new in this version  [1,500]

    First release.

    - Renders locally for the IsHaunted.com video editor: split, join, thumbnails, media inspection
      and final encode.
    - An on/off switch in the editor. Turning it off mid-render asks you to confirm rather than
      dropping the export.
    - H.264 export through Windows' own built-in encoder, and VP9.

## Product features  [up to 20, 200 each]

    Exports run on your processor instead of inside a browser tab, so long clips finish in a
    fraction of the time
    Your footage is rendered on the computer it is already on - nothing is uploaded in order to be
    rendered
    An on/off switch inside the video editor, which will not stop a render out from under you
    Listens only on this computer, and only answers the editor
    Does not run at startup unless you turn it on yourself in Windows Settings
    Free, with a free IsHaunted.com account

## Search terms  [up to 7 terms, 30 chars each, 21 words total]

    ghost hunting
    paranormal investigation
    ghost hunter
    offline rendering
    transcode
    companion app
    local encoder

Seven terms, 13 words, longest 24 characters - inside all three limits.

NOT a list of what the app does. Microsoft's guidance is that search terms are for words which are
NOT already in the app's name or description, because those are searchable already; the first set
here spent four of the seven on "video", which the description says a dozen times, and one on
"ishaunted", which is in the product NAME - where a title match already outranks any keyword.

So the first three are AUDIENCE, not function. Ben's buyers are ghost hunters, and the word
"paranormal" appears nowhere in this listing, which means that search cannot currently reach the
app at all. A search for "ghost hunting" in the Store returns almost nothing, so the app lands in
a near-empty result set rather than competing with every video tool published.

The last four are function words this listing genuinely does not use: it says "encoding" but never
"encoder" or "transcode", and never "offline" or "companion".

Re-check this list whenever the description changes. Its whole value is being the words that are
missing from everything else.

## Additional license terms  [10,000]

    This application bundles FFmpeg (version 9.0.1), which is licensed under the GNU Lesser General
    Public License. The bundled build is configured with --enable-version3, so parts of it are
    covered by LGPL version 3 rather than version 2.1. FFmpeg is not owned by ISHAUNTED LLC and is
    used unmodified.

    Source code for the bundled FFmpeg build, and the text of the licence, are available from
    https://ffmpeg.org and from the build's publisher at
    https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-08-31-13-27 (file
    ffmpeg-n9.0.1-11-ge47273f4d9-win64-lgpl-9.0.zip). A copy will also be provided on request to
    the support address in this listing.

## Copyright and trademark info  [200]

    Copyright 2026 ISHAUNTED LLC. All rights reserved.

## Developed by  [255]

    ISHAUNTED LLC

---

# The other pages

**Properties**
- Category: **Photo & video** is the closest fit. Utilities & tools is a defensible second choice.
- Privacy policy URL: `https://ishaunted.com/privacy` (checked live, 200, 2026-09-23)
- Website: `https://ishaunted.com`
- Support contact info: `https://ishaunted.com/contact` (checked live, 200; there is no /support)
- System requirements: nothing to declare. The app is self-contained .NET and needs no framework
  package; the manifest already floors it at Windows 10 1809.

**Age ratings** - no objectionable content, no user-generated content shared through this app, no
gambling, no in-app purchase, no data shared with third parties. Note the banner: IARC changed the
questionnaire, so sections may come back marked incomplete even after a first pass.

**Pricing and availability** - Free. Markets: all. The app is English-only, but a market is where
it can be bought, not what language it speaks.

**Packages** - upload `installer/dist/BenVideoSidecar-win-x64.msix`. Do NOT sign it; Microsoft
re-signs Store packages, which is the entire reason this route was chosen.

**Screenshots** - at least one is mandatory, minimum 1366x768. SideCar has no window, so the
screenshot has to be the editor with the SideCar switch in it. That is the one part of this
submission that cannot be written in advance.

---

# Why the description is shaped the way it is

Store policy expects an app to carry its own value, and a reviewer meeting a background helper with
no window reasonably asks what it is for. The first two paragraphs answer that before anything else
gets said: the browser has a hard ceiling, this removes it, and the work happens where the files
already are. The "no window" fact is stated plainly under HOW IT WORKS rather than left to be
discovered, and the fallback is named - the editor still works without it - because a reviewer who
cannot install the website's account flow needs to know the app is an accelerator, not a gate.

If certification does push back, the fix is here, not in the code.
