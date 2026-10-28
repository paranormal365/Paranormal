# Test fixtures

| File | What it is | Used by |
|---|---|---|
| `field-session-stage.ben` | A real bundle from the phone's own exporter: 18 s, one `audio-001.m4a`, one H.264 `video-001.mov`, three photos, a sealed `seal.json`. | Stage, EVP scan, event and help-capture tests |
| `video-only-vp9.mp4` | 20 s with no sound track, VP9 in MP4, 320×180. The stage bundle's own video looped to 20 s and re-encoded, because Playwright's Chromium has no H.264. A session with only this file is the shape of Ben's 10/23/2026 session (the phone lost its sound file). | `FieldSessionTransportTests` |
| `device-clip.m4a` | 3 s cut from a real device recording with `-c copy`, so `moov` stays at the end as the phone writes it. | Audio editor, decoder tests |
| `room-photo-1.jpg` | A photo for a session's photo strip. | `FieldSessionEngineTests` |
| `test-audio.mp3` | A long MP3 for the audio editor. | Audio editor tests |

Remaking `video-only-vp9.mp4` (the stage bundle is a stored zip):

```bash
unzip -o -q field-session-stage.ben media/video-001.mov -d /tmp/stage
ffmpeg -stream_loop 2 -i /tmp/stage/media/video-001.mov -t 20 -an -c:v libvpx-vp9 -b:v 120k -movflags +faststart video-only-vp9.mp4
```
