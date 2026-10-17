# fix/ios-negative-zero-mg

Found 10/17/2026 while retaking the 1.1.4 help pictures: the live meter on an iPad read "-0 mG".

**Cause.** `String(format: "%+.0f", -0.4)` is "-0": the sign is chosen before the rounding. The same
pattern drew the meter's big number, its tick labels, the session review's Field tile and the trim
preview, and `"%.0f"` could print "-0 dB" for a very loud sound. VoiceOver would have read the same
reading as "0 mG below base".

**Fix.** `BenKit/Field/ReadingText.swift`: `signedWhole` ("+20", "-3", "0"), `whole` ("-51", "0")
and `fromBase` ("3 mG above base", "at base"), each rounding first. Used by AnalogMeterView,
SessionReviewView, TrimPreview, FieldGauges and LiveSessionView. Tests: `ReadingTextTests`, including
one that pins the old format's "-0" so the reason stays on record.

**Ships with the next app version** (Unreleased line in `Changelog/Content/ios.md`). Build and
`scripts/warnings.sh` clean; BenKit 585 tests pass.
