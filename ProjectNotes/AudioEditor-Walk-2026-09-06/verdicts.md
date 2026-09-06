# Audio editor walk — verdicts

| time | finding | verdict | observed |
|---|---|---|---|
| 06:14:00 | compact | **PASS** | compact waveform rendered after upload |
| 06:14:01 | A | **PASS** | full view opened from the context menu |
| 06:14:02 | A | **PASS** | after X: 0 modal(s) still shown |
| 06:14:03 | A | **FAIL** | modal came back on its own after the compact player re-rendered the parent |
| 06:14:40 | regions | **PASS** | regions after 1st draw: 1, after 2nd: 1 (design: one user region at a time) |
| 06:14:40 | edit-target | **PASS** | Cut enabled with a drawn region: True |
| 06:14:40 | edit-target | **NOTE** | edit panel region readout: 1:14.6–1:33.2 |
| 06:14:43 | B | **NOTE** | regions with silence detection on: 0; edit readout now: 3:00.6–3:06.5 |
| 06:14:43 | B | **FAIL** | the edit target moved to a machine-detected region |
| 06:14:44 | B | **PASS** | regions after drawing a user region with silence on: 0 (was 0) |
| 06:14:55 | C | **NOTE** | toolbar selects visible with spectrogram on: 3 |
| 06:15:31 | D | **NOTE** | EQ panel checkboxes: 4; each toggled and screenshot taken |
| 06:15:32 | D | **CODE** | no handler beyond @bind on the four enable checkboxes (AudioFilePreview.razor:186,191,200,212); effect not observable from outside |
| 06:15:40 | edit:Cut | **PASS** | produced saved clip #1 |
| 06:17:10 | edit:Silence | **FAIL** | no new saved clip within 60 s; error shown: (none) |
| 06:17:12 | edit:Normalize | **PASS** | produced saved clip #2 |
| 06:17:15 | edit:Reverse | **PASS** | produced saved clip #3 |
| 06:17:17 | edit:Apply Gain | **PASS** | produced saved clip #4 |
| 06:17:19 | edit:Apply Fade | **PASS** | produced saved clip #5 |
| 06:17:38 | edit:Apply Speed | **PASS** | produced saved clip #6 |
| 06:17:56 | edit:Apply Pitch | **PASS** | produced saved clip #7 |
| 06:17:57 | F | **PASS** | 7 edits made; 0 of 7 saved-clip badges read 0:00–0:00: [0:00.0–0:00.0; 0:00.0–0:00.0; 0:00.0–0:00.0; 0:00.0–0:00.0; 0:00.0–0:00.0; 0:00.0–0:00.0; 0:00.0–0:00.0] |
| 06:19:16 | scan | **PASS** | scan message: 21 candidates to review. |
| 06:19:18 | review | **PASS** | kept a candidate through the confirm dialog |
| 06:19:54 | K | **PASS** | audio clips offered by the mixer: 7 |
| 06:19:56 | K | **NOTE** | after 9 adds: 9 clip blocks on the grid; refusal message shown: False |
| 06:19:56 | K | **FAIL** | clip block widths: 120 (all equal means length is not real) |
| 06:19:56 | K | **FAIL** | Play disabled: True |
| 06:21:17 | compact | **PASS** | compact waveform rendered after upload |
| 06:21:18 | A | **PASS** | full view opened from the context menu |
| 06:21:19 | A | **PASS** | after X: 0 modal(s) still shown |
| 06:21:20 | A | **FAIL** | modal came back on its own after the compact player re-rendered the parent |
| 06:21:21 | A | **NOTE** | after Escape on the resurrected modal: 1 modal(s) shown |
| 06:21:32 | C | **NOTE** | toolbar selects visible with spectrogram on: 3 |
| 06:21:32 | C | **NOTE** | colormap select index 1, resolution select index 0 |
| 06:21:38 | C | **FAIL** | colormap change repainted: jet=0,0,0 viridis=0,0,0 |
| 06:21:46 | C | **FAIL** | resolution change reverted the colormap toward jet: 0,0,0 |
| 06:22:28 | scan | **PASS** | scan message: 21 candidates to review. |
| 06:22:30 | review | **FAIL** | the Keep dialog stayed open after Keep it |
| 06:22:32 | J | **FAIL** | after marker ▶: media playing=False, Pause button visible=False |
| 06:22:38 | K | **PASS** | audio clips offered by the mixer: 11 |
| 06:22:40 | K | **NOTE** | after 9 adds: 9 clip blocks on the grid; refusal message shown: False |
| 06:22:40 | K | **FAIL** | clip block widths: 120 (all equal means length is not real) |
| 06:22:40 | K | **FAIL** | Play disabled: True |
| 06:22:48 | K | **FAIL** | the clip block swallows the click meant for its ✕ — remove is unreachable by mouse |
| 06:23:33 | K | **PASS** | export finished and returned to the case |
| 06:23:37 | K-perm | **NOTE** | Viewer sees the Audio Mixer button on the case: False |
| 06:24:30 | I | **PASS** | explorer closed with its X |
| 06:25:01 | H | **NOTE** | second region not reachable: Timeout 30000ms exceeded.
Call log:
  - waiting for Locator(".modal.show").First.Locator("[part~='region']").First
    - locator resolved to <div part="region region-3hk49l50uh">…</div>
  - attempting click action
    2 × waiting for element to be visible, enabled and stable
      - element is visible, enabled and stable
      - scrolling into view if needed
      - done scrolling
      - <div>…</div> from <div tabindex="-1" role="dialog" aria-modal="true" class="modal fade show d-block" _bl_166db0b6-299f-4856-8f2c-1e21172f42f0="" aria-label="Region Explorer — 0:18.65 – 0:27.97">…</div> subtree intercepts pointer events
    - retrying click action
    - waiting 20ms
    2 × waiting for element to be visible, enabled and stable
      - element is visible, enabled and stable
      - scrolling into view if needed
      - done scrolling
      - <div>…</div> from <div tabindex="-1" role="dialog" aria-modal="true" class="modal fade show d-block" _bl_166db0b6-299f-4856-8f2c-1e21172f42f0="" aria-label="Region Explorer — 0:18.65 – 0:27.97">…</div> subtree intercepts pointer events
    - retrying click action
      - waiting 100ms
    58 × waiting for element to be visible, enabled and stable
       - element is visible, enabled and stable
       - scrolling into view if needed
       - done scrolling
       - <div>…</div> from <div tabindex="-1" role="dialog" aria-modal="true" class="modal fade show d-block" _bl_166db0b6-299f-4856-8f2c-1e21172f42f0="" aria-label="Region Explorer — 0:18.65 – 0:27.97">…</div> subtree intercepts pointer events
     - retrying click action
       - waiting 500ms |
| 06:25:08 | scan | **PASS** | scan message: 21 candidates to review. |
| 06:25:10 | review | **PASS** | kept a candidate through the confirm dialog |
| 06:25:11 | J | **FAIL** | after marker ▶: media playing=False, Pause button visible=False |
