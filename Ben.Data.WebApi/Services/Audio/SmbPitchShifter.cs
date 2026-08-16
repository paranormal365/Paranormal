namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// FFT phase-vocoder pitch shifter, ported from Stephan M. Bernsee's public-domain
/// "Pitch Shifting Using The Fourier Transform" (1999) reference implementation.
/// Processes one mono channel of audio; construct one instance per channel.
/// </summary>
internal sealed class SmbPitchShifter
{
    private const int MaxFrameLength = 8192;

    private readonly int _fftFrameSize;
    private readonly int _osamp;
    private readonly double _sampleRate;
    private readonly int _inFifoLatency;

    private readonly float[] _inFifo;
    private readonly float[] _outFifo;
    private readonly double[] _fftWorksp;
    private readonly double[] _lastPhase;
    private readonly double[] _sumPhase;
    private readonly double[] _outputAccum;
    private readonly double[] _anaFreq;
    private readonly double[] _anaMagn;
    private readonly double[] _synFreq;
    private readonly double[] _synMagn;
    private int _rover;

    public int InFifoLatency => _inFifoLatency;

    public SmbPitchShifter(int fftFrameSize, int osamp, double sampleRate)
    {
        if (fftFrameSize > MaxFrameLength)
            throw new ArgumentOutOfRangeException(nameof(fftFrameSize), $"Must be <= {MaxFrameLength}.");

        _fftFrameSize = fftFrameSize;
        _osamp = osamp;
        _sampleRate = sampleRate;
        _inFifoLatency = fftFrameSize - fftFrameSize / osamp;
        _rover = _inFifoLatency;

        _inFifo = new float[MaxFrameLength];
        _outFifo = new float[MaxFrameLength];
        _fftWorksp = new double[2 * MaxFrameLength];
        _lastPhase = new double[MaxFrameLength / 2 + 1];
        _sumPhase = new double[MaxFrameLength / 2 + 1];
        _outputAccum = new double[2 * MaxFrameLength];
        _anaFreq = new double[MaxFrameLength];
        _anaMagn = new double[MaxFrameLength];
        _synFreq = new double[MaxFrameLength];
        _synMagn = new double[MaxFrameLength];
    }

    /// <summary>
    /// Pitch-shifts <paramref name="numSampsToProcess"/> samples of <paramref name="indata"/> by
    /// <paramref name="pitchShift"/> (1.0 = no change, 2.0 = up one octave, 0.5 = down one octave)
    /// into <paramref name="outdata"/>. Output is delayed by <see cref="InFifoLatency"/> samples
    /// relative to the input — pad the input with that many trailing zero samples and discard the
    /// same number of leading output samples to get a fully aligned, flushed result.
    /// </summary>
    public void PitchShift(double pitchShift, float[] indata, float[] outdata, int numSampsToProcess)
    {
        var fftFrameSize2 = _fftFrameSize / 2;
        var stepSize = _fftFrameSize / _osamp;
        var freqPerBin = _sampleRate / _fftFrameSize;
        var expct = 2.0 * Math.PI * stepSize / _fftFrameSize;
        var inFifoLatency = _inFifoLatency;

        for (var i = 0; i < numSampsToProcess; i++)
        {
            _inFifo[_rover] = indata[i];
            outdata[i] = _outFifo[_rover - inFifoLatency];
            _rover++;

            if (_rover >= _fftFrameSize)
            {
                _rover = inFifoLatency;

                for (var k = 0; k < _fftFrameSize; k++)
                {
                    var window = -0.5 * Math.Cos(2.0 * Math.PI * k / _fftFrameSize) + 0.5;
                    _fftWorksp[2 * k] = _inFifo[k] * window;
                    _fftWorksp[2 * k + 1] = 0.0;
                }

                Fft(_fftWorksp, _fftFrameSize, -1);

                for (var k = 0; k <= fftFrameSize2; k++)
                {
                    var real = _fftWorksp[2 * k];
                    var imag = _fftWorksp[2 * k + 1];
                    var magn = 2.0 * Math.Sqrt(real * real + imag * imag);
                    var phase = Math.Atan2(imag, real);

                    var tmp = phase - _lastPhase[k];
                    _lastPhase[k] = phase;
                    tmp -= k * expct;

                    var qpd = (long)(tmp / Math.PI);
                    if (qpd >= 0) qpd += qpd & 1; else qpd -= qpd & 1;
                    tmp -= Math.PI * qpd;

                    tmp = _osamp * tmp / (2.0 * Math.PI);
                    tmp = k * freqPerBin + tmp * freqPerBin;

                    _anaMagn[k] = magn;
                    _anaFreq[k] = tmp;
                }

                Array.Clear(_synMagn, 0, _fftFrameSize);
                Array.Clear(_synFreq, 0, _fftFrameSize);
                for (var k = 0; k <= fftFrameSize2; k++)
                {
                    var index = (int)(k * pitchShift);
                    if (index <= fftFrameSize2)
                    {
                        _synMagn[index] += _anaMagn[k];
                        _synFreq[index] = _anaFreq[k] * pitchShift;
                    }
                }

                for (var k = 0; k <= fftFrameSize2; k++)
                {
                    var magn = _synMagn[k];
                    var tmp = _synFreq[k];
                    tmp -= k * freqPerBin;
                    tmp /= freqPerBin;
                    tmp = 2.0 * Math.PI * tmp / _osamp;
                    tmp += k * expct;
                    _sumPhase[k] += tmp;
                    var phase = _sumPhase[k];
                    _fftWorksp[2 * k] = magn * Math.Cos(phase);
                    _fftWorksp[2 * k + 1] = magn * Math.Sin(phase);
                }

                for (var k = _fftFrameSize + 2; k < 2 * _fftFrameSize; k++) _fftWorksp[k] = 0.0;

                Fft(_fftWorksp, _fftFrameSize, 1);

                for (var k = 0; k < _fftFrameSize; k++)
                {
                    var window = -0.5 * Math.Cos(2.0 * Math.PI * k / _fftFrameSize) + 0.5;
                    _outputAccum[k] += 2.0 * window * _fftWorksp[2 * k] / (fftFrameSize2 * _osamp);
                }
                for (var k = 0; k < stepSize; k++) _outFifo[k] = (float)_outputAccum[k];

                Array.Copy(_outputAccum, stepSize, _outputAccum, 0, _fftFrameSize);

                for (var k = 0; k < inFifoLatency; k++) _inFifo[k] = _inFifo[k + stepSize];
            }
        }
    }

    /// <summary>In-place complex FFT/IFFT on an interleaved [re0, im0, re1, im1, ...] buffer. sign = -1 forward, +1 inverse.</summary>
    private static void Fft(double[] fftBuffer, long fftFrameSize, long sign)
    {
        for (long i = 2; i < 2 * fftFrameSize - 2; i += 2)
        {
            long j;
            long bitm;
            for (bitm = 2, j = 0; bitm < 2 * fftFrameSize; bitm <<= 1)
            {
                if ((i & bitm) != 0) j++;
                j <<= 1;
            }
            if (i < j)
            {
                (fftBuffer[i], fftBuffer[j]) = (fftBuffer[j], fftBuffer[i]);
                (fftBuffer[i + 1], fftBuffer[j + 1]) = (fftBuffer[j + 1], fftBuffer[i + 1]);
            }
        }

        var kMax = (long)(Math.Log2(fftFrameSize) + 0.5);
        long le = 2;
        for (long k = 0; k < kMax; k++)
        {
            le <<= 1;
            var le2 = le >> 1;
            double ur = 1.0, ui = 0.0;
            var arg = Math.PI / (le2 >> 1);
            var wr = Math.Cos(arg);
            var wi = sign * Math.Sin(arg);
            for (long j = 0; j < le2; j += 2)
            {
                for (long i = j; i < 2 * fftFrameSize; i += le)
                {
                    var p1r = i;
                    var p1i = i + 1;
                    var p2r = i + le2;
                    var p2i = p2r + 1;
                    var tr = fftBuffer[p2r] * ur - fftBuffer[p2i] * ui;
                    var ti = fftBuffer[p2r] * ui + fftBuffer[p2i] * ur;
                    fftBuffer[p2r] = fftBuffer[p1r] - tr;
                    fftBuffer[p2i] = fftBuffer[p1i] - ti;
                    fftBuffer[p1r] += tr;
                    fftBuffer[p1i] += ti;
                }
                var trw = ur * wr - ui * wi;
                ui = ur * wi + ui * wr;
                ur = trw;
            }
        }
    }
}
