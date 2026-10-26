# Audio phase 4, last step: phone recordings in the editor (10/26/2026)

Branch `feature/audio-phase-4-m4a-ui`, off develop after the phase 4 decoder merge (`24cbc0e2`).
Plan: `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 4 step 6. Ben: "let .m4a recordings
be scanned and clipped from the media library and the editor, showing the server's reason when it
refuses."

## What was actually in the way

The plan expected the screens to filter .m4a out of Scan and Make clip. They never did: the media
library and the editor only ask whether a file is `audio/*`. What stood between a phone recording
and the editor was elsewhere:

1. **The editor's scan dropped the server's reason.** It called `ScanAudioForEvpAsync`, which turns
   every refusal into null, and then said "The scan failed. The file may not be readable audio."
   about a recording that was fine. It now calls `ScanAudioForEvpWithReasonAsync` and shows the
   server's sentence ("This server cannot read .m4a yet.") followed by "This recording has not been
   examined."
2. **Every edit refused an .m4a** ("Audio is supported for WAV and MP3"). Phase 4 had left the edit
   controller out. `UploadFileAudioEditController` now takes `IAudioDecoder`: the length comes from
   the decoder, and an .m4a is decoded to WAV at its own rate and channels before the unchanged edit
   code runs. A recording longer than the edit limit is refused from its header, with the same
   sentence a long MP3 gets (`AudioSourceReader.EnsureEditable`), before anything is decoded.
3. Clipping already worked and already showed its reason (phase 4).

## Bugs fixed on the way

- **Edits never checked the free allowance.** The edit door was on the guard test's exemption list
  because "an edit of bytes already counted" never "grows a file materially". It always did: every
  edit is saved as WAV, about ten times an MP3 of the same length. It now asks
  `AccountStorageGuard.WhyCannotStoreAsync` before writing, like clips, and the exemption is gone.
- **A latent race in the unit tests.** `AudioSourceReaderTests` lowers the static
  `MaximumEditDuration` to one second while the edit tests decode two-second files in parallel. Both
  classes are now in `AudioEditLimitCollection`, which runs alone.
- **ffmpeg's path can be relative.** `MediaToolOptions.ResolveAgainst` (a `PostConfigure` in
  `Program.cs`) takes a relative `FfmpegPath` from the API's own folder, so on MonsterASP it can be
  `App_Data\Tools\ffmpeg.exe` like the key files, without knowing where the host keeps the site.

## Tests

- Unit: `Edit_OfAnM4a_IsReadThroughTheDecoder`, `Edit_OfAnM4a_PastTheEditLimit_IsRefusedWithTheSameSentence`,
  `Edit_PastTheFreeAllowance_IsRefusedBeforeAnythingIsWritten`, `MediaToolOptionsTests` (3).
- e2e: `AudioEditorTests.A_phone_recording_can_be_scanned_and_edited` uploads the real device clip
  (`Ben.Web.Playwright/Fixtures/device-clip.m4a`, index after the audio) as `audio/x-m4a`, scans it,
  and normalizes it. It ignores itself, quoting the editor, on a host with no ffmpeg.

## Deploy: ffmpeg on MonsterASP

No migration. The API needs ffmpeg for phone recordings. The same setting also turns on two things
that have been quietly off without it: removing location and device details from uploaded audio and
video (item 181), and the automatic check of video posts (F5b).

1. On your Windows PC, download **ffmpeg-release-essentials.zip** from
   https://www.gyan.dev/ffmpeg/builds/ (the "release builds" section).
2. Unzip it. The only file needed is `bin\ffmpeg.exe` (about 100 MB, no DLLs beside it).
3. In MonsterASP's File Manager (or FTP, if the File Manager refuses a file that size), open the **API** site (api.ishaunted.com), go into
   `App_Data`, make a folder `Tools`, and upload `ffmpeg.exe` into it. `App_Data` is never served
   to visitors, and it is where `Keys` already lives.
4. Open the API site's `web.config` (in the site's root folder, beside `Ben.Data.WebApi.dll`). Inside
   `<aspNetCore ...><environmentVariables>`, where `Smtp__Password` and
   `ConnectionStrings__BenDbConnectionString` already are, add one line:
   `<environmentVariable name="MediaTools__FfmpegPath" value="App_Data\Tools\ffmpeg.exe" />`
   Save. If you keep a copy of this `web.config` on your PC for deploys, add the line there too.
5. Deploy this build (the relative path needs it), or restart the API site if it is already deployed.
6. Sign in, open `/admin`, and read the **Audio decoding** card. It shows ffmpeg's version, or says
   what is wrong: not set, no file there, or the host would not let it run. Send that sentence back
   if it is not a version.

If a deploy wipes the site folder, `App_Data\Tools` goes with it, the same as `App_Data\Keys`; put it
back the same way.
