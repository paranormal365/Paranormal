namespace Ben.Web.Website.Library.Organization.Cms;

/// <summary>
/// The CMS's version: the pages a group builds for its public site. It has no project of its own —
/// it lives in the website's library — so its number is kept here.
/// </summary>
/// <remarks>
/// Ben, 10/14/2026: "track the CMS as its own project as well, like the video and audio editor."
/// This is the newest released heading of <c>Ben.Web.Services/Changelog/Content/cms.md</c>, and
/// changes only at release, in the same commit that turns "## Unreleased" into "## x.y.z"
/// (ChangelogServiceTests).
/// </remarks>
public static class CmsRelease
{
    public const string Version = "1.0.0";
}
