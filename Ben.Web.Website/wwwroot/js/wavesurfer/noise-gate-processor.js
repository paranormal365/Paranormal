/**
 * noise-gate-processor.js — AudioWorklet processor for noise gating.
 * Attenuates the signal when amplitude falls below the threshold.
 */
class NoiseGateProcessor extends AudioWorkletProcessor {
    static get parameterDescriptors() {
        return [
            { name: 'threshold', defaultValue: -40, minValue: -100, maxValue: 0 },
            { name: 'attack',    defaultValue: 0.01, minValue: 0.0001, maxValue: 1 },
            { name: 'release',   defaultValue: 0.15, minValue: 0.001,  maxValue: 2 },
        ]
    }

    constructor() {
        super()
        this._gain    = 1.0
        this._enabled = true
        this.port.onmessage = (e) => {
            if (e.data?.enabled !== undefined) this._enabled = e.data.enabled
        }
    }

    process(inputs, outputs, parameters) {
        const input  = inputs[0]
        const output = outputs[0]
        if (!input?.length) return true

        const threshold = parameters.threshold.length > 1 ? parameters.threshold : parameters.threshold[0]
        const attack    = parameters.attack.length   > 1 ? parameters.attack    : parameters.attack[0]
        const release   = parameters.release.length  > 1 ? parameters.release   : parameters.release[0]

        const blockSize      = input[0].length
        // Per-sample smoothing coefficients derived from time constants
        const attackCoef  = Math.exp(-1 / (typeof attack  === 'number' ? attack  : attack[0])  / sampleRate)
        const releaseCoef = Math.exp(-1 / (typeof release === 'number' ? release : release[0]) / sampleRate)
        const threshDb    = typeof threshold === 'number' ? threshold : threshold[0]
        const threshLin   = Math.pow(10, threshDb / 20)

        for (let channel = 0; channel < output.length; channel++) {
            const inCh  = input[channel]  ?? new Float32Array(blockSize)
            const outCh = output[channel]
            for (let i = 0; i < blockSize; i++) {
                const level      = Math.abs(inCh[i])
                const targetGain = (!this._enabled || level >= threshLin) ? 1.0 : 0.0
                this._gain       = targetGain > this._gain
                    ? 1 - (1 - this._gain) * attackCoef
                    : this._gain * releaseCoef

                outCh[i] = inCh[i] * this._gain
            }
        }
        return true
    }
}
registerProcessor('noise-gate-processor', NoiseGateProcessor)
