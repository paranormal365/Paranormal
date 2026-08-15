# IsHaunted Device Data Format — v1.0.0

*Status: draft specification. Written 2026-08-15. No importer exists yet — see [Implementation status](#implementation-status).*

A JSON format for delivering readings from an investigation device — EMF meter, thermal probe,
motion logger, environmental sensor — into a case file.

- **Schema**: [`device-data-v1.schema.json`](device-data-v1.schema.json) (JSON Schema draft 2020-12)
- **Examples**: [`examples/`](examples/) — all three validate against the schema
- **Audience**: device makers and anyone writing an exporter. You should be able to implement
  against this document alone.

---

## Why this exists

Investigators arrive with instruments from a dozen manufacturers, and each one exports something
different: a CSV with unlabelled columns, a proprietary binary, a screenshot of a display. The
readings end up retyped into notes, and by the time anyone reviews the case the numbers have lost
the things that made them meaningful — what the baseline was, whether the device was moving, how
accurate the clock was.

This format exists so a reading arrives with the context needed to judge it.

## Design rules

These are the decisions everything else follows from. They are stated because a reader
implementing an exporter needs to know *why*, not just *what*.

**1. A bare number is not evidence.** Any numeric measurement must carry a `unit`. The schema
enforces this — a numeric `value` without a `unit` is invalid. Where the device knows them,
`accuracy` and `baseline` turn an absolute figure into something a reviewer can weigh: 4.8 µT means
nothing until you know the ambient was 0.3.

**2. Everything except the timestamp is optional.** Devices report what they can. A logger with no
GPS omits `position` entirely; that is not an error and must never be reported as one. Only `at` is
required on a reading, because a measurement without a time cannot be placed against anything else.

**3. Say how precise the clock is.** A device stamping whole seconds declares
`"precision": "second"` rather than emitting `.000` and implying accuracy it does not have.
Correlating an EMF spike with an audio event is exactly the analysis that a silently-overstated
timestamp ruins.

**4. Gaps must be interpretable.** `session.trigger` says how readings came to exist. Under
`interval`, a gap means the device missed a sample. Under `event`, a gap means *nothing happened* —
which is itself a finding. Without this, the two are indistinguishable and neither can be trusted.

**5. Extend through `measurements`, never through new top-level fields.** A new instrument adds a
key to the `measurements` map. This is why the map is keyed by channel name instead of the format
carrying an `emf` field, a `temperature` field, and so on forever.

**6. Consumers ignore unknown keys.** Every object permits additional properties. A file written
against 1.3 must still load in a 1.0 reader, with the unrecognised parts skipped rather than
rejected. This is verified: a document carrying invented keys validates.

## Conventions

| Rule | Detail |
|---|---|
| Timestamps | ISO-8601 **with offset**, UTC. `2026-08-14T22:05:07Z`. Never local time without an offset. |
| Local time | Put the IANA zone in `session.timezone` if it matters. Timestamps stay UTC regardless. |
| Keys | `snake_case`, lowercase, ASCII. |
| Units | ASCII symbols — `uT` not `µT`, `degC` not `°C`. Encoding damage in transit is common and silent. |
| Versioning | `format_version` is semver **for this format**, not the firmware. Additive changes bump minor; a breaking change bumps major and gets its own document. |
| Null vs absent | Treated identically. Write whichever is convenient. |
| Ordering | `readings` oldest first. |
| File names | Companion files are relative paths inside the delivered bundle. Absolute paths and `..` are rejected by the schema. |

## Structure

```
{
  format_version   "1.0.0"
  device           { manufacturer, model, serial_number?, firmware_version? }
  session          { started_at, ended_at?, device_powered_on_at?,
                     battery_percent_at_start?, location_label?, property_area?,
                     timezone?, trigger { … } }
  readings         [ { at, precision?, sequence?, triggered_by?,
                       measurements?, position?, motion?, audio_ref?, note? } ]
}
```

### `device`

Identifies the instrument, not the operator. `manufacturer` and `model` are required — an
unattributed reading cannot be assessed for known quirks, and every meter has some.

`serial_number` may be null: some devices have none, and some operators would rather not record it.

### `session`

`started_at` and `trigger` are required; everything else is context that helps a reviewer decide
how much to trust the numbers.

Two fields exist for reasons that are not obvious:

- **`device_powered_on_at`** — several instruments drift until warm. Knowing a session began forty
  seconds after power-on is sometimes the whole explanation for its first readings.
- **`battery_percent_at_start`** — low battery is a documented cause of spurious readings on
  several common EMF meters. A reviewer seeing 8% reads the spikes differently.

`location_label` is the operator's own words ("back bedroom, north wall"). `property_area` is an
optional structured tag for grouping the same place across sessions and devices.

### `session.trigger`

| `mode` | Required alongside | Meaning |
|---|---|---|
| `interval` | `interval_seconds` | Sampled on a fixed period. |
| `event` | `event_description` | Recorded only when something crossed a threshold. |
| `hybrid` | both | Events, plus a periodic heartbeat so silence is distinguishable from a dead device. |

`event_description` is deliberately free text: threshold semantics differ too much between devices
to enumerate, and a human reviewer is the consumer. Write what actually causes a record — *"field
exceeds 2.0 uT above baseline"*, *"PIR motion within 4 m"*.

`debounce_seconds` is the minimum quiet period between event records. Null means the device does
not debounce, which tells a reviewer that a burst of records may be one event.

### `readings[]`

Only `at` is required.

- **`sequence`** — a device-assigned counter. Its value is detecting *dropped* records, which
  timestamps alone cannot reveal: a gap might be silence or might be loss, and the counter
  distinguishes them.
- **`triggered_by`** — under `hybrid`, separates a heartbeat from a real event. Without it every
  heartbeat looks like a detection.
- **`note`** — the operator's remark about this specific moment.

### `measurements`

A map of channel name to a measurement object. Suggested channel names — conventions, not a closed
list:

`emf`, `emf_x` / `emf_y` / `emf_z`, `temperature`, `humidity`, `pressure`, `illuminance`,
`sound_level`, `radiation`, `ion_count`, `motion_detected`, `battery`

Each measurement carries:

| Field | Notes |
|---|---|
| `value` | number, string, boolean, or null. **Required.** |
| `unit` | **Required when `value` is a number.** ASCII symbol. |
| `accuracy` | ± in the same unit. |
| `baseline` | The ambient reference this was calibrated against, same unit. |
| `out_of_range` | True when the sensor pegged and the real value is unknown. Do not report a clipped maximum as a measurement. |

### `position`, `motion`, `audio_ref`

**`position`** — indoor GPS is usually poor, so `accuracy_meters` is what tells a consumer whether
to believe it. `floor` disambiguates vertically stacked rooms that GPS cannot separate.

**`motion`** — a field spike recorded while the meter was being swung is a different fact from one
recorded while it sat still. `is_stationary` is the device's own judgement where it makes one.

**`audio_ref`** — a companion file in the same bundle. `start_offset_seconds` places this reading's
moment within a longer recording. `sha256` lets a consumer prove the pairing survived transit;
mismatched audio attached to the wrong reading is worse than no audio.

## Delivery

A submission is either a single `.json` file, or a `.zip` containing `data.json` at the root plus
any companion files at the relative paths named in `audio_ref.filename`.

The schema forbids absolute paths and `..` in those names. That is a security boundary, not a
style preference: an importer expanding a bundle must never be steered outside its own directory.

## Validation

```bash
pip install jsonschema
python3 -c "
import json
from jsonschema import Draft202012Validator
schema = json.load(open('ProjectNotes/specs/device-data-v1.schema.json'))
doc    = json.load(open('ProjectNotes/specs/examples/01-emf-meter-with-audio.json'))
errs   = list(Draft202012Validator(schema).iter_errors(doc))
print('VALID' if not errs else [ (list(e.path), e.message) for e in errs ])
"
```

The schema was checked against 14 deliberately malformed documents — missing `format_version`, an
`interval` trigger with no period, a numeric measurement with no unit, latitude 130, a `..` path in
a filename, a malformed digest — and rejects all of them, while still accepting a document carrying
unknown keys. A schema that accepts everything would validate nothing.

## Implementation status

**Nothing imports this yet.** It is a specification so device makers can build against a stable
target before the import path exists.

When import is built, readings land as `CaseTimelineEntry` rows of type `InstrumentReading` — the
entry type added in Area 5 / C3 specifically to hold them. `session.location_label` maps naturally
onto the entry title, and each reading's `at` onto `EventDateTime`. The timeline already orders
tied timestamps deterministically, which matters here because interval-sampled devices routinely
produce several readings that share a second.

Open questions deliberately left for the import work, not decided here:

- Whether one session becomes one timeline entry or one entry per reading. A five-hour interval log
  is thousands of readings; one entry each would bury the case timeline.
- Whether unrecognised `measurements` channels are stored verbatim or dropped.
- Who may upload device data — investigator only, or clients too.

## Changelog

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2026-08-15 | First draft. |
