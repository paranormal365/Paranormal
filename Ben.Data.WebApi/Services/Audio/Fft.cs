namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// A plain radix-2 FFT, moved unchanged out of <see cref="SmbPitchShifter"/> (audio phase 11, 10/27/2026) so the
/// EVP features can use the same one. <c>FftTests</c> pins the pitch shifter's output across the move.
/// </summary>
public static class Fft
{
    /// <summary>In-place complex FFT/IFFT on an interleaved [re0, im0, re1, im1, ...] buffer. sign = -1 forward, +1 inverse.</summary>
    public static void Transform(double[] fftBuffer, long fftFrameSize, long sign)
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
