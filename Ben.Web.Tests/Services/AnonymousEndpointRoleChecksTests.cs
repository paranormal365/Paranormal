using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An <c>[AllowAnonymous]</c> action may not decide anything from <c>User.IsInRole</c>.
/// </summary>
/// <remarks>
/// <para><b>Why only the anonymous ones.</b> <c>UseAuthentication</c> populates <c>User</c> from
/// the <i>default</i> scheme alone — the local Identity bearer handler. Where an action requires
/// authentication that is harmless: the default policy pins both schemes, so the authorization
/// middleware authenticates Entra too, the claims transformation runs, and <c>User</c> is replaced
/// with the merged principal carrying its database roles. **79 of the 81 role checks in the
/// controllers are on such actions and are correct.**</para>
///
/// <para><b>On an <c>[AllowAnonymous]</c> action nothing does that.</b> A caller signed in with
/// Microsoft arrives with no principal at all — not lacking the role, unauthenticated — so
/// <c>User.IsInRole</c> is false and the endpoint serves them the visitor's view. Two endpoints
/// were written that way, and one of them 404ed a SuperAdmin out of the unapproved catalogue model
/// they were there to review. Item 140.</para>
///
/// <para><b>Both failed closed</b> — an admin saw less, never more — which is why this was a
/// visibility gap rather than a security hole, and why it went unnoticed.</para>
///
/// <para>Use <c>BenControllerBase.CallerIsSuperAdminAsync()</c>, which asks the local claim first
/// and then authenticates the Entra scheme explicitly.</para>
/// </remarks>
public sealed class AnonymousEndpointRoleChecksTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>
    /// Walks back from a line to the method that contains it and returns that method's attributes.
    /// </summary>
    private static string AttributesOfEnclosingMethod(string[] lines, int index)
    {
        var signature = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:public|private|internal|protected)\s+(?:async\s+)?[\w<>,\[\]\?\. ]+\s+\w+\s*\(");

        var j = index;
        while (j > 0 && !signature.IsMatch(lines[j])) j--;

        var attributes = new List<string>();
        var k = j - 1;
        while (k >= 0)
        {
            var text = lines[k].Trim();
            if (text.StartsWith('[')) attributes.Add(text);
            else if (text.Length != 0 && !text.StartsWith("///")) break;
            k--;
        }

        return string.Join(" ", attributes);
    }

    [Fact]
    public void An_anonymous_action_does_not_read_a_role_off_the_principal()
    {
        var offenders = new List<string>();

        var controllers = Directory.EnumerateFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "Controllers"),
            "*.cs", SearchOption.AllDirectories);

        foreach (var file in controllers.OrderBy(f => f))
        {
            var lines = StripComments(File.ReadAllText(file)).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("User.IsInRole(", StringComparison.Ordinal)) continue;
                if (!AttributesOfEnclosingMethod(lines, i).Contains("AllowAnonymous", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These [AllowAnonymous] actions decide something from User.IsInRole:

               {string.Join("\n  ", offenders)}

             On an anonymous endpoint User comes from the default scheme only, so a caller signed
             in with Microsoft is unauthenticated rather than merely unprivileged, and the check
             silently says no. Use BenControllerBase.CallerIsSuperAdminAsync(), which authenticates
             the Entra scheme explicitly. Item 140.
             """);
    }
}
