# Audio editor walk — verdicts

| time | finding | verdict | observed |
|---|---|---|---|
| 07:05:47 | C | **NOTE** | toolbar selects visible with spectrogram on: 3 |
| 07:05:47 | C | **NOTE** | colormap select index 1, resolution select index 0 |
| 07:05:53 | C | **PASS** | colormap change repainted: jet=6,7,145 viridis=71,13,91 |
| 07:06:02 | C | **PASS** | colormap survived the resolution change: 70,21,95 (jet=6,7,145, viridis=71,13,91) |
| 07:06:31 | C-mel | **NOTE** | brightness centroid — linear 0.523, mel 0.529, mel after a resolution change 0.529 |
| 07:06:31 | C-mel | **NOTE** | mel moved the centroid by 0.006 |
| 07:06:31 | C-mel | **PASS** | the mel scale survived the resolution change |
| 07:06:31 | S | **NOTE** | canvases in the modal: 1408x128 |
| 07:06:31 | S | **CONSOLE** | warning: Canvas2D: Multiple readback operations using getImageData are faster with the willReadFrequently attribute set to true. See: https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-will-read-frequently |
