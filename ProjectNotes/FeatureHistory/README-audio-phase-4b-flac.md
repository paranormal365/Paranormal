# Audio phase 4b: clips, edits and mixes saved as FLAC (10/26/2026)

Branch `feature/audio-phase-4b-flac`, off develop after the phase 4 last step (`37fdbf13`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 4b.

## Why

Every clip and edit was saved as 16-bit WAV, about ten times the size of the MP3 or .m4a it came
from, and since phase 4 every byte of it counts against a free account's allowance. Ben first asked
about saving as MP3 with the size difference shown; MP3 throws away the quietest sounds, which is
what an EVP search listens for, and an .m4a converted to MP3 would lose twice. Ben: "I would not
recommend a format that loses information." FLAC keeps every sample of the WAV in a smaller file
(18.9 MB against 31.4 MB on the e2e recording; quieter room recordings shrink more), and browsers
play it.

## What changed

- **`AudioSaveFormat`** (`Flac` = default, `Wav`) on `ClipAudioRequest`, `AudioEditRequest` and
  `ExportAudioMixRequest`. No MP3, on purpose.
- **`IAudioEncoder`**, implemented by `FfmpegAudioDecoder` (same ffmpeg, same scratch-file pattern):
  `TryWavToFlacAsync` at compression level 8. A failure is logged and the save stays WAV.
- **`AudioSave.AsAsync`**, the last step of the clip, edit and mix controllers. With no ffmpeg, or
  WAV asked for, the bytes are untouched. Metadata is measured off the WAV the FLAC came from, with
  `AudioCodec` "FLAC".
- **`UploadFileRecord.WavEquivalentBytes`**: set only on a clip or edit's answer, never stored, so the
  page can name both sizes.
- **Reading FLAC back.** `MediaDuration` reads a FLAC's length from its STREAMINFO, so a clip of a
  FLAC clip is checked before decoding. The decoder already read FLAC through ffmpeg.
- **The case mixer** reads tracks through the decoder (`AudioDecoderExtensions.AsReadableAsync`,
  shared with the edit controller), so FLAC clips and phone recordings can be mixed. Before this, a
  FLAC clip would have broken every mix it was put in. The mix itself is saved as FLAC.
- **The editor.** One choice, `SaveFormatChoice` ("Save as WAV instead of FLAC (a larger file, the same
  sound)"), in the Make clip dialog, the Edit tab and the region explorer. After a save the
  notice and a line at the top of the Clips tab (`#audio-last-save`) say, for example, "Saved as
  FLAC, 4.1 MB (as WAV it would be 9.8 MB). Nothing is lost: it plays back exactly the same."
  (`AudioSaveSummary`). The dialog and explorer buttons read **Save clip**, no longer "Save as WAV".

## Tests

- `FlacSaveTests`: a FLAC from ffmpeg decodes to exactly the WAV's samples, rate, channels and bit
  depth (a noisy tone, since silence would prove nothing); it is smaller; its length comes from the
  header; no ffmpeg or WAV asked for leaves the bytes alone; the three sentences.
- Controllers: `Clip_IsSavedAsFlac_ByDefault_AndSaysWhatTheWavWouldHaveBeen` (also the FLAC metadata
  row and its duration), `Clip_AskedForAsWav_StaysWav`, `Edit_IsSavedAsFlac_ByDefault`,
  `Export_OfAFlacClip_IsReadThroughTheDecoder_AndSavedAsFlac`.
- e2e `An_edit_is_saved_as_flac_and_says_how_big_each_format_is`: Normalize, the note names both
  sizes, the new clip's row draws the FLAC's waveform (the browser decoded it), then WAV from the
  Edit tab. It said "Saved as FLAC, 18.9 MB (as WAV it would be 31.4 MB). Nothing is lost: it plays
  back exactly the same." then "Saved as WAV, 31.4 MB."
- Audio + mixer + content picker e2e: 32 of 32. Unit tests: 8,952 pass.

## Deploy

No migration. FLAC needs the same ffmpeg as phone recordings (`README-audio-phase-4-m4a-ui.md`);
without it everything is saved as WAV, as before, and the page says so.
