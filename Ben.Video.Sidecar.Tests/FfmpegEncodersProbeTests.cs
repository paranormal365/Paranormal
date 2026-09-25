using Ben.Video.Sidecar.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// The probe against a real ffmpeg, when this checkout has one bundled.
/// </summary>
/// <remarks>
/// <see cref="FfmpegEncodersTests"/> pins the parsing against listings copied from real builds,
/// which is what catches a regression. This asks the actual binary, which is what catches the
/// listing having moved on since those were copied.
///
/// <para>Passes quietly, saying so in its output, where no binary is bundled: the binaries are
/// fetched by <c>fetch-ffmpeg.sh</c> and are deliberately not in the repository, so a clean
/// checkout has none. A test that failed there would be reporting on the checkout, not on the
/// code.</para>
/// </remarks>
public sealed class FfmpegEncodersProbeTests(ITestOutputHelper output)
{
    private static string? BundledDir()
    {
        // The test binary sits under <project>/bin/<config>/<tfm>; ffmpeg is bundled beside the
        // sidecar's own project, and is copied next to the test output when it is present.
        var candidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        return Directory.Exists(candidate) ? AppContext.BaseDirectory : null;
    }

    [Fact]
    public void A_real_ffmpeg_reports_encoders_this_can_read()
    {
        var baseDir = BundledDir();
        if (baseDir is null)
        {
            output.WriteLine("No ffmpeg bundled in this checkout - run fetch-ffmpeg.sh. Nothing asked.");
            return;
        }

        var locator = new FfmpegLocator(baseDir);
        if (!File.Exists(locator.ExecutablePath))
        {
            output.WriteLine($"No ffmpeg binary for {locator.Rid}. Nothing asked.");
            return;
        }

        var names = new FfmpegEncoders(locator, NullLogger<FfmpegEncoders>.Instance).Names;
        output.WriteLine($"{locator.Rid}: {names.Count} encoders.");

        // Not an assertion about which encoders: only that the listing was understood at all.
        Assert.NotEmpty(names);
        Assert.Contains("aac", names);          // in every build there has ever been
        Assert.DoesNotContain("=", names);      // the header's legend, if parsing regressed
        Assert.All(names, n => Assert.True(char.IsLetter(n[0]), $"'{n}' is not an encoder name."));
    }
}
