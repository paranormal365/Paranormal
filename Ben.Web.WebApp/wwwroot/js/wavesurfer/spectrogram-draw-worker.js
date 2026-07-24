/**
 * spectrogram-draw-worker.js
 * Renders a spectrogram slice into an RGBA pixel buffer off the main thread.
 * Uses an O(W × H) output-pixel loop instead of iterating over all input frames,
 * so render time is constant regardless of the number of FFT frames.
 *
 * Receives:
 *   { flat: Float32Array, nFrames, nBins, width, height, version, cacheKey }
 *   flat     — row-major [frame0bin0, frame0bin1, …, frame1bin0, …] (transferred)
 *
 * Posts back:
 *   { pixels: Uint8ClampedArray, width, height, version, cacheKey }
 *   pixels   — RGBA flat buffer (width × height × 4)  (transferred)
 */
self.onmessage = (e) => {
  const { flat, nFrames, nBins, width, height, version, cacheKey } = e.data

  // Normalise across the visible slice
  let maxMag = 1e-9
  for (let i = 0; i < flat.length; i++)
    if (flat[i] > maxMag) maxMag = flat[i]

  const pixels = new Uint8ClampedArray(width * height * 4)

  for (let px = 0; px < width; px++) {
    const frameIdx  = Math.min(nFrames - 1, Math.floor(px * nFrames / width))
    const frameBase = frameIdx * nBins
    for (let py = 0; py < height; py++) {
      // Flip Y so low frequencies are at the bottom
      const binIdx = nBins - 1 - Math.min(nBins - 1, Math.floor(py * nBins / height))
      const t = flat[frameBase + binIdx] / maxMag
      // Viridis-inspired: dark-navy → cyan → yellow
      const r = Math.floor(255 * Math.pow(t, 0.5))
      const g = Math.floor(255 * Math.min(1, t * 1.5))
      const b = Math.floor(255 * Math.max(0, 0.9 - t))
      const idx = (py * width + px) * 4
      pixels[idx]     = r
      pixels[idx + 1] = g
      pixels[idx + 2] = b
      pixels[idx + 3] = 255
    }
  }

  self.postMessage({ pixels, width, height, version, cacheKey }, [pixels.buffer])
}
