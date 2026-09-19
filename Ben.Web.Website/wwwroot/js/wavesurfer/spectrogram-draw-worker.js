/**
 * spectrogram-draw-worker.js
 * Renders a spectrogram slice into an RGBA pixel buffer off the main thread.
 *
 * Receives:
 *   { flat, nFrames, nBins, width, height, version, cacheKey, colormap?, melScale?, sampleRate? }
 *   flat      — row-major Float32Array [frame0bin0, …] (transferred)
 *   colormap  — optional 256-entry [[r,g,b,a], …] array (default: jet-like)
 *   melScale  — optional boolean; when true Y-axis uses mel-scale frequency mapping
 *   sampleRate — required when melScale=true
 *
 * Posts back:
 *   { pixels: Uint8ClampedArray, width, height, version, cacheKey }
 */

function hz2mel(hz) { return 2595 * Math.log10(1 + hz / 700) }
function mel2hz(m)  { return 700 * (Math.pow(10, m / 2595) - 1) }

self.onmessage = (e) => {
  const { flat, nFrames, nBins, width, height, version, cacheKey,
          colormap, melScale, sampleRate = 44100 } = e.data

  // Normalise across the slice
  let maxMag = 1e-9
  for (let i = 0; i < flat.length; i++)
    if (flat[i] > maxMag) maxMag = flat[i]

  const fMax   = sampleRate / 2
  const melMax = hz2mel(fMax)

  // Pre-compute bin index per Y-pixel row (once, not per column)
  const binMap = new Int32Array(height)
  for (let py = 0; py < height; py++) {
    if (melScale) {
      // Top = high freq → bottom = low freq, mel-spaced
      const fracFromTop = py / (height - 1)
      const mel  = melMax * (1 - fracFromTop)
      const freq = mel2hz(mel)
      binMap[py] = Math.min(nBins - 1, Math.max(0, Math.round(freq / fMax * nBins)))
    } else {
      binMap[py] = nBins - 1 - Math.min(nBins - 1, Math.floor(py * nBins / height))
    }
  }

  const pixels = new Uint8ClampedArray(width * height * 4)

  for (let px = 0; px < width; px++) {
    const frameIdx  = Math.min(nFrames - 1, Math.floor(px * nFrames / width))
    const frameBase = frameIdx * nBins
    for (let py = 0; py < height; py++) {
      const t   = flat[frameBase + binMap[py]] / maxMag
      const idx = (py * width + px) * 4
      if (colormap) {
        const ci = Math.min(255, Math.floor(t * 255))
        const c  = colormap[ci]
        pixels[idx]     = Math.round(c[0] * 255)
        pixels[idx + 1] = Math.round(c[1] * 255)
        pixels[idx + 2] = Math.round(c[2] * 255)
      } else {
        // fallback jet-like
        pixels[idx]     = Math.floor(255 * Math.pow(t, 0.5))
        pixels[idx + 1] = Math.floor(255 * Math.min(1, t * 1.5))
        pixels[idx + 2] = Math.floor(255 * Math.max(0, 0.9 - t))
      }
      pixels[idx + 3] = 255
    }
  }

  self.postMessage({ pixels, width, height, version, cacheKey }, [pixels.buffer])
}
