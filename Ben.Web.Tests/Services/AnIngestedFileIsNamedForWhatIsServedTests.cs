using System.Text.RegularExpressions;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A picture re-encoded as JPEG is recorded under a .jpg name (crawl C5, 2026-09-21).
/// </summary>
/// <remarks>
/// Sanitizing re-encodes every picture as JPEG, and every upload path recorded the uploaded name —
/// so <c>corridor.png</c> was served as JPEG bytes under a .png name, and a download opened in a
/// tool that trusts the extension got the wrong decoder. The feed did it from the day ingest was
/// built; place evidence inherited it.
/// </remarks>
public sealed class AnIngestedFileIsNamedForWhatIsServedTests
{
    private static IngestedMedia Served(string contentType, bool sanitized = true)
        => new(new UploadFileMetadata(), 1, contentType, sanitized);

    [Theory]
    [InlineData("corridor.png", "corridor.jpg")]
    [InlineData("Scan 01.HEIC", "Scan 01.jpg")]
    [InlineData("no-extension", "no-extension.jpg")]
    [InlineData("already.jpeg", "already.jpeg")]
    [InlineData("already.JPG", "already.JPG")]
    public void A_picture_served_as_jpeg_is_named_as_one(string uploaded, string recorded)
        => Assert.Equal(recorded, Served("image/jpeg").ServedFileName(uploaded));

    /// <summary>What is served as uploaded keeps the uploaded name — video, audio, a PDF.</summary>
    [Theory]
    [InlineData("porch.mp4", "video/mp4")]
    [InlineData("evp.m4a", "audio/mp4")]
    [InlineData("report.pdf", "application/pdf")]
    public void What_is_served_as_uploaded_keeps_its_name(string uploaded, string contentType)
        => Assert.Equal(uploaded, Served(contentType, sanitized: false).ServedFileName(uploaded));

    /// <summary>
    /// Every upload path that records the served type records the served name beside it.
    /// </summary>
    /// <remarks>
    /// Seventeen callers each wrote their own <c>FileName = file.FileName</c>, which is how one
    /// fault lived in all of them. A new upload path that takes the type and not the name fails.
    /// </remarks>
    [Fact]
    public void Every_upload_that_records_the_served_type_records_the_served_name()
    {
        var root = RepoFiles.Root().FullName;
        var api = Path.Combine(root, "Ben.Data.WebApi") + Path.DirectorySeparatorChar;
        var offenders = new List<string>();

        foreach (var path in RepoFiles.Paths("*.cs").Where(p => p.StartsWith(api, StringComparison.Ordinal)))
        {
            var lines = NoCredentialsInLogsTests.WithoutComments(File.ReadAllText(path)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"ContentType\s*=\s*\w+\.ServedContentType")) continue;

                // The initializer around it: a few lines either way, as every caller writes it.
                var around = string.Join("\n", lines[Math.Max(0, i - 8)..Math.Min(lines.Length, i + 8)]);
                if (!Regex.IsMatch(around, @"\bFileName\s*=\s*\w+\.ServedFileName\("))
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These record the served content type but not the served name, so a re-encoded picture "
          + "keeps its uploaded extension. Use FileName = ingested.ServedFileName(…):\n  "
          + string.Join("\n  ", offenders));
    }
}
