# App Store media — IsHaunted 1.0.3

Ten frames per device and one preview per device, dark, captured through a real scripted night on
the simulator. Run with one command per device:

```
Ben.iOS/scripts/capture-app-store-media.sh iphone 1.0.3
Ben.iOS/scripts/capture-app-store-media.sh ipad   1.0.3
```

It refuses to start without an API at `localhost:5252`, because the Send screen signs in — without
one, frame 14 photographs the signed-out fallback rather than the trimmer.

## What is new since 1.0.2

**`15-fieldkit-camera` — the camera is the app's own.** In 1.0.2, taking a photograph handed the
microphone to Apple's camera, which stopped the recording and left a hole in the sound. Now the
capture happens inside the session: the sound carries straight through, and a clip's own audio
fills the gap while it runs. There was no screen worth photographing before; this is it.

The preview shows the same thing happening rather than only the needle swinging — the drive takes a
photograph part way through and carries on.

Everything else in the set is re-captured from the current build rather than carried over, so no
frame shows a screen that has since changed.

## Sizes

| | Captured | Uploaded |
|---|---|---|
| iPhone 17 Pro Max | 1320 × 2868 | 1242 × 2688 (`sips` resize then crop, in the script) |
| iPad Pro 13-inch | 2064 × 2752 | as captured |

## The preview

Cut with `tools/preview.swift` (AVFoundation: probe / poster / cut — fill and crop, H.264, video
only) because **this Mac has no ffmpeg on the path**; the vendored one at
`Ben.Video.Sidecar/ffmpeg/osx-arm64/ffmpeg` adds the audio track afterwards.

**A silent audio track is not optional.** App Review refused the 1.0.0 previews on 2026-09-09 —
*"Your app preview contains unsupported or corrupted audio"* — for files that had no audio stream at
all. Both previews carry a silent AAC track, added with `-c:v copy` so the captured picture is
untouched.

Windows: iPhone `[30 s, +28]`, iPad `[27 s, +28]`, chosen from poster frames.

## Two things the capture taught, still true

- `TEST_RUNNER_*` variables must be **exported in the shell**. Passed as `xcodebuild KEY=value` they
  never reach the runner, and the test skips itself while the build reports success.
- The screenshot test **relaunches after `-autoSignIn` lands**, so the Keychain-restored session is
  the one used. Left as launched, the iPad reached the Send screen with "Your session ended".

## Before uploading

Look at every frame. The capture is scripted; the judgement is not. In particular: no Developer
section (a Debug build shows one, and debug UI in a screenshot is a rejection), no wrapped clock,
and the trimmer frame showing an in point somewhere other than the very start.
