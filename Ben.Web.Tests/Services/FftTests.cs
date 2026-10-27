using System.Security.Cryptography;
using Ben.Data.WebApi.Services.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The FFT shared by the pitch shifter and the EVP features (audio phase 11, 10/27/2026).
/// </summary>
public sealed class FftTests
{
    /// <summary>
    /// The pitch shifter's output, byte for byte, pinned BEFORE its FFT moved into <see cref="Fft"/>:
    /// the move must change nothing anybody hears.
    /// </summary>
    [Fact]
    public void The_pitch_shifter_sounds_exactly_as_it_did_before_the_move()
    {
        var input = new float[16_000];
        for (var i = 0; i < input.Length; i++)
            input[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 440 * i / 16_000.0) + 0.2 * Math.Sin(2 * Math.PI * 1234 * i / 16_000.0));
        var output = new float[input.Length];

        new SmbPitchShifter(2048, 4, 16_000).PitchShift(1.5, input, output, input.Length);

        var bytes = new byte[output.Length * sizeof(float)];
        Buffer.BlockCopy(output, 0, bytes, 0, bytes.Length);
        Assert.Equal(PinnedOutput, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private const string PinnedOutput = "F255361F63F450C062025D933A6C7B3003E87FD77F1D2F8F86930BE5BFD941B1";
}
