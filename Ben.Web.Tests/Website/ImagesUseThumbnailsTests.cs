using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A picture on screen is fetched as a thumbnail, not as the whole upload.
/// </summary>
/// <remarks>
/// <para>Every <c>&lt;img&gt;</c> on the site used to point at <c>/download</c>, which serves the
/// original bytes. A group logo drawn in a 40px box pulled the entire upload down the wire — at
/// whatever size it was uploaded — and the browser discarded nearly all of it. The browse page
/// lists twenty groups, so that is twenty full-size images to draw twenty thumbnails, on a page a
/// first-time visitor is likely to open on a phone.</para>
///
/// <para>Nothing about that is visible in development, where the seeded logos are a few kilobytes
/// and the API is on localhost. It shows up as a slow site for real people with real photographs,
/// which is the worst kind of regression to leave to observation.</para>
///
/// <para><b>Only <c>src</c> is checked.</b> An <c>&lt;a href&gt;</c> pointing at
/// <c>/download</c> is correct — that is somebody asking for the file — and so is a media
/// player's source. This is specifically about images rendered into a layout.</para>
/// </remarks>
public sealed class ImagesUseThumbnailsTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> RazorFiles()
    {
        var root = RepoRoot().FullName;
        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                yield return file;
            }
        }
    }

    [Fact]
    public void No_image_src_points_at_the_full_download_url()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var source = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(source, @"src=""@[A-Za-z]*\.?GetFileDownloadUrl"))
            {
                var line = source.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These images fetch the whole upload to draw a small picture. Use "
            + "GetFileThumbnailUrl — same access rules, and a non-image falls through to the real "
            + "file anyway:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_thumbnail_url_is_actually_in_use()
    {
        // Guards the guard. If GetFileThumbnailUrl were renamed away, the test above would pass
        // over a site that had quietly gone back to full-size images.
        var users = RazorFiles().Count(f =>
            File.ReadAllText(f).Contains("GetFileThumbnailUrl", StringComparison.Ordinal));

        Assert.True(users >= 5,
            $"Only {users} components use GetFileThumbnailUrl — did the images move back to /download?");
    }
}
