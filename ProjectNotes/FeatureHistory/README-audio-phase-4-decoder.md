# Audio phase 4 (S): read .m4a anywhere; one scan recorder

Branch: `claude/serene-mcclintock-7942a1`, cut from `develop` at `c9f5651f`. Plan of record:
`~/.claude/plans/okay-so-lets-go-splendid-spring.md`, "Phase 4 (S)". Steps 1–5 and 7 are here.
**Step 6 (the editor and `MediaLibraryGrid` no longer filtering .m4a out of Scan / Make clip) is
NOT here.** Another session is restructuring the editor's files, so that step goes with it.

## Why

Session audio is AAC in an `.m4a` (`audio/mp4`). The server's only decoder, `AudioSourceReader`,
read MP3 and WAV, so a phone recording could not be scanned for EVPs or clipped at all. Production
already has ffmpeg (`MediaTools:FfmpegPath`) for the feed's video screening.

## What is here

- **`Services/Audio/IAudioDecoder`**, with three implementations:
  - `NAudioDecoder`: MP3/WAV through `AudioSourceReader`, unchanged and always available.
  - `FfmpegAudioDecoder`: anything ffmpeg reads. It copies the source to
    `{temp}/ben-evp-{guid}{ext}` (same pattern as `AvMetadataStripper`), then runs
    `ffmpeg -nostdin -hide_banner -loglevel error -i <file> -vn -map 0:a:0 -f s16le -ac 1 -ar 16000 pipe:1`.
    Ranges are written to a `.wav` **file**, because a WAV written to a pipe carries a 0xFFFFFFFF
    length that NAudio refuses. A non-zero exit or a timeout (`MediaTools:DecodeTimeoutSeconds`,
    default 600) throws `AudioDecodeFailedException` with ffmpeg's last lines. It is a
    `FormatException`, so every audio endpoint already answers it with a 400. The scratch file is
    always deleted.
  - `CompositeAudioDecoder` (the registered `IAudioDecoder`): NAudio for MP3/WAV, so those decode
    exactly as before and the pinned EVP scores don't move; ffmpeg for other `audio/*`. With no
    ffmpeg it throws `AudioDecoderUnavailableException("This server cannot read .m4a yet.")`. Any
    other unread type gets a sentence that names it rather than calling it an .m4a.
- **`Services/Audio/EvpScanRecorder`**: `ReplaceCandidates`, the scan's candidate proposal and
  `TryRecordScanAsync`, lifted out of `AudioMarkerController` with the same rules: only Pending
  markers are replaced, delete and insert happen in one transaction, nothing overlapping an
  already-reviewed span is re-proposed, the 500 cap applies, and the `EvpScan` record is written
  after the candidates and may fail. `ProposeAsync(db, fileId, analysis, sensitivity, settings,
  createdBy, ct, noteFor)` is the entry point the background jobs and session scans (phases 6
  and 9) will call. Auditing stays in the controller.
- **`AudioMarkerController.Scan`** reads through `IAudioDecoder.ReadMono16kAsync`, then calls
  `EvpDetector.Analyze(float[], …)`. With no ffmpeg, an .m4a is refused with a **400 carrying
  `ScanRefusalRecord(Reason: "DecoderUnavailable", Message)`**, never a bare sentence. The undecodable
  filter now also uses `AudioSourceReader.IsUndecodable`, like the clip and edit endpoints.
- **`UploadFileAudioClipController`**: `Clip` gets its length (`DurationSecondsAsync`, which uses
  the MP3/WAV header or `MediaDuration` for .m4a) and its range (`DecodeRangeToWavAsync`) from the
  decoder. `ClipPreview` reads the range the same way. **`AccountStorageGuard.WhyCannotStoreAsync`
  now runs before the write.** A clip is stored under the person, so a free account at its cap
  gets a 413 with the guard's sentence, and members of a paid group are exempt, as the guard
  already decides.
- **`GET api/admin/media-tools`** (`AdminMediaToolsController`, SuperAdmin policy) returns
  `MediaToolsHealth(FfmpegConfigured, Path, Found, Version, Problem)`. It runs `ffmpeg -version`, so
  a path that exists but cannot run is also reported. `/admin` now has an **"Audio decoding"** card:
  either "ffmpeg 9.0.2 at /path. Recordings from the phone can be scanned and clipped." or the
  problem, e.g. "Not configured: .m4a scans and clips are refused until MediaTools:FfmpegPath is set."
- **Client:** `ScanAudioForEvpWithReasonAsync` (on `IWebApiClient` and `IBenMediaClient`) returns
  the refusal's sentence. The plain prose reader drops anything that starts with `{`, so without
  this method the editor could not show "This server cannot read .m4a yet." The editor session
  should switch its Scan button to this method; until then the old method still returns null.
- **`scripts/run-e2e.sh`** exports `MediaTools__FfmpegPath=$(command -v ffmpeg)` for the API host
  when ffmpeg is on the machine.

## Tests

- `Fixtures/device-clip.m4a`: cut with `ffmpeg -t 3 -c copy` from `media/audio-001.m4a` inside
  `Ben.Web.Playwright/Fixtures/field-session-stage.ben`.
  **Note: the source in that bundle actually has `moov` BEFORE `mdat`** (`ftyp, moov, free, mdat`).
  The cut has it after (`ftyp, free, mdat, moov`), because ffmpeg writes `moov` last unless told
  `+faststart`. `DeviceClip_KeepsTheMovieIndexAfterTheAudio` pins that order.
- **The 3-second clip does not tell a pipe from a file.** ffmpeg 9.0.2 reads it from a real pipe
  without complaint, because the whole file fits in its read buffer. A 33-second, 98 KB file with
  `moov` last fails from a pipe with "partial file". `ReadMono16k_ReadsALongRecordingWhoseIndexIsAtTheEnd`
  builds that file at run time (`-stream_loop 10 -c copy`) and reads it through a forward-only
  stream.
- `FfmpegAudioDecoderTests` (10): box order; an ffprobe guard (aac, about 3 s); a whole read; a
  forward-only stream; the long moov-last recording; chunks equal a whole read; a range read back as
  a WAV; duration from the header; a broken file failing with ffmpeg's reason; scratch files
  deleted. Each test skips with a message when the machine has no ffmpeg or ffprobe.
- `CompositeAudioDecoderTests` (5): the .m4a sentence, .ogg not being called an .m4a, MP3 still
  decoding without ffmpeg, `CanDecode`, and non-audio refused as not audio.
- `AudioMarkerControllerTests`: an .m4a is read through the decoder, and with no ffmpeg it is
  refused with a `ScanRefusalRecord`.
- `UploadFileAudioClipControllerTests`: an .m4a is clipped through the decoder, a range past the
  end is refused, and the free-allowance refusal returns 413 with nothing written.
- `AudioApiRoutesTests`: the client reads the refusal record's sentence, and the prose sentence too.

**Each test was run against the old behavior and failed:**
- Swapping the decoder to `-i pipe:0` (stdin fed from the file) failed
  `ReadMono16k_ReadsALongRecordingWhoseIndexIsAtTheEnd` (3 s of audio from a 33 s file). Every
  other ffmpeg test still passed, which is why that test exists.
- Putting the old calls back in the controllers (`EvpDetector.Analyze(stream…)`,
  `AudioSourceReader.Probe`, `AudioClipper.Clip`) and removing the storage check failed all five
  new controller tests.

`dotnet test Ben.Web.Tests --filter "Evp|AudioMarker|AudioClip|AudioDecoder"`: 187 passed, 0
skipped (ffmpeg 9.0.2 at /opt/homebrew/bin).

## Deploy notes

- **No migration.**
- **`MediaTools:FfmpegPath` must be set** on the API for .m4a. *Corrected 10/26/2026:* this said
  production already sets it in `scripts/deploy-ishaunted.ps1`. That script sets it on the old
  self-hosted IIS box only; on MonsterASP it is the API site's environment variable
  `MediaTools__FfmpegPath`, and whether it was ever set there is unknown. Steps are in
  `README-audio-phase-4-m4a-ui.md`. After deploy, `/admin` → "Audio decoding" shows a version or
  says what is wrong.
- New optional setting: `MediaTools:DecodeTimeoutSeconds` (default 600).
- The API writes `ben-evp-*` scratch files to the system temp folder while decoding and deletes them
  afterwards. The IIS app pool identity must be able to write there (it already does for the
  `ben-strip-*` metadata stripping).
- New DI registrations in `Program.cs`, next to `AvMetadataStripper`: `NAudioDecoder`,
  `FfmpegAudioDecoder`, `IAudioDecoder → CompositeAudioDecoder` (singletons) and `EvpScanRecorder`
  (scoped).
- Restart the API so the new controller and services load.

## Left for later

- Step 6 (UI) and the destructive edit: done on `feature/audio-phase-4-m4a-ui`, see
  `README-audio-phase-4-m4a-ui.md`.
- The case mixer still reads MP3/WAV only.
