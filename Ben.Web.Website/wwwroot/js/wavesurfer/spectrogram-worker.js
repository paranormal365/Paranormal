/**
 * spectrogram-worker.js
 * ─────────────────────
 * Web Worker that computes spectrogram (FFT magnitude) data from raw audio
 * samples, keeping the main thread responsive during analysis of long files.
 *
 * Input message:
 *   { channels: Float32Array[], sampleRate: number, fftSamples: number, noverlap: number }
 *
 * Progress messages posted while computing:
 *   { type: 'progress', percent: number }
 *
 * Completion message (Float32Array buffers transferred to avoid copying):
 *   { type: 'done', data: Float32Array[], sampleRate: number, fftSamples: number }
 *   data[i] = magnitude spectrum for frame i  (fftSamples/2 positive-frequency bins)
 */

// ── Cooley-Tukey radix-2 in-place FFT ────────────────────────────────────────

function fft(re, im) {
  const n = re.length
  // Bit-reversal permutation
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1
    for (; j & bit; bit >>= 1) j ^= bit
    j ^= bit
    if (i < j) {
      ;[re[i], re[j]] = [re[j], re[i]]
      ;[im[i], im[j]] = [im[j], im[i]]
    }
  }
  // Butterfly stages
  for (let len = 2; len <= n; len <<= 1) {
    const ang = -2 * Math.PI / len
    const wRe = Math.cos(ang)
    const wIm = Math.sin(ang)
    for (let i = 0; i < n; i += len) {
      let curRe = 1, curIm = 0
      for (let j = 0; j < len / 2; j++) {
        const uRe = re[i + j], uIm = im[i + j]
        const vRe = re[i + j + len / 2] * curRe - im[i + j + len / 2] * curIm
        const vIm = re[i + j + len / 2] * curIm + im[i + j + len / 2] * curRe
        re[i + j]           = uRe + vRe
        im[i + j]           = uIm + vIm
        re[i + j + len / 2] = uRe - vRe
        im[i + j + len / 2] = uIm - vIm
        const nCurRe = curRe * wRe - curIm * wIm
        curIm        = curRe * wIm + curIm * wRe
        curRe        = nCurRe
      }
    }
  }
}

// ── Main ──────────────────────────────────────────────────────────────────────

self.onmessage = function (e) {
  const { channels, sampleRate, fftSamples, noverlap } = e.data

  const channel  = channels[0]
  const hop      = fftSamples - noverlap
  const numFrames = Math.max(1, Math.floor((channel.length - fftSamples) / hop) + 1)

  const re     = new Float32Array(fftSamples)
  const im     = new Float32Array(fftSamples)
  const result = []

  self.postMessage({ type: 'progress', percent: 0 })

  const reportEvery = Math.max(1, Math.floor(numFrames / 20))

  for (let frame = 0; frame < numFrames; frame++) {
    const offset = frame * hop

    // Fill buffers with Hann-windowed samples
    for (let j = 0; j < fftSamples; j++) {
      const sample = offset + j < channel.length ? channel[offset + j] : 0
      const hann   = 0.5 * (1 - Math.cos(2 * Math.PI * j / fftSamples))
      re[j] = sample * hann
      im[j] = 0
    }

    fft(re, im)

    // Positive-frequency magnitude spectrum
    const mag = new Float32Array(fftSamples / 2)
    for (let j = 0; j < fftSamples / 2; j++) {
      mag[j] = Math.sqrt(re[j] * re[j] + im[j] * im[j])
    }
    result.push(mag)

    if (frame % reportEvery === 0) {
      self.postMessage({ type: 'progress', percent: Math.round(100 * frame / numFrames) })
    }
  }

  // Transfer all magnitude buffers (zero-copy) back to the main thread
  self.postMessage(
    { type: 'done', data: result, sampleRate, fftSamples },
    result.map(m => m.buffer)
  )
}
