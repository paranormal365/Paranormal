# The automatic screener — item 186 F5b

F5 shipped the safety gate's structure: media fail-closed by the data model (`Pending` never
serves), the Moderator role, the review queue, and a screening seam whose shipping implementation
deliberately approves nothing — every photo and video waits for a person. That is safe and honest,
and it does not scale, and nothing in the feed arc may launch before screening is automatic.

This branch plugs the real classifier into the seam.

## What this builds

- **`OnnxNsfwScreener`** — an on-device NSFW classifier (the ONNX export of
  `Falconsai/nsfw_image_detection`, ViT, Apache-2.0, two classes) run in-process over every feed
  photo and every sampled video frame. Nothing leaves the server — which is not just cost control:
  item 184's private-residence footage must never be sent to a third-party moderation API.
- **The asymmetric decision map** (`NsfwDecision`): confident-clean approves (< 0.30), everything
  above goes to a person (`Held`), confident-porn is blocked outright (≥ 0.85) with the score in
  the moderator-facing note. Approving is the irreversible act, so only it demands confidence.
- **Video sampling**: frames at 1 fps (capped at 12) plus the final second, through the same
  `MediaTools:FfmpegPath` binary the ingest pipeline already uses. The worst frame decides. No
  ffmpeg configured ⇒ videos stay Pending with a reason — we did not look, and Pending is the only
  honest word — and the sweep picks them up once a tool is configured.
- **`PendingMediaScreeningJob`** — the recovery path for anything stuck Pending (screener down,
  process died mid-create, no ffmpeg, the F4→F5b backlog). Runs on the existing five-minute
  scheduler, oldest-first in bounded batches, and never overrides a decision a person already made.
- **Loud degradation**: the model file is fetched (`scripts/get-screener-model.sh` / `.ps1`),
  not committed (87 MB). Missing model ⇒ the manual screener registers instead, the startup log
  WARNS, and `/admin/feed-reports` already shows "screening is not automatic" via `IsAutomatic`.

## The contract that makes this safe to get wrong

Unchanged from F5, and the reason this branch is small: `Pending` is the default state and is
never served to anybody. A screener that throws, times out, or is absent leaves media exactly
where it started. The worst any bug here can do is grow a queue a person works through — never
publish something nobody looked at.

## Deployment note

The model must be present on the machine that produces the publish output — the conditional
`Content` item in `Ben.Data.WebApi.csproj` then carries it into publish. The deploy script is
deliberately untouched by this branch (it was being fixed for an unrelated issue at the time);
running `scripts/get-screener-model.ps1` once on the build host is the whole requirement, and the
startup log line states the screening posture on every boot either way.

## Verifying

- Unit: decision-map boundaries (0.30/0.85, both edges), softmax, preprocessing golden values
  (the documented ViT contract: 224×224, 1/255, (x−0.5)/0.5), ffmpeg argument shapes, sweep-job
  behavior (skips under manual screener; approves/holds/leaves-pending; never overrides a
  person's decision), undecodable-image ⇒ Held, no-ffmpeg-video ⇒ Pending.
- Integration (skipped when the model file is absent): the real model over the repo's neutral
  generated test images ⇒ every one Approved — the false-positive gate, in the spirit of the EVP
  detector's fixture gate.
- Live: post a photo through the real API with the screener registered and watch it arrive
  `Approved` with the score in the review note, no human involved.
