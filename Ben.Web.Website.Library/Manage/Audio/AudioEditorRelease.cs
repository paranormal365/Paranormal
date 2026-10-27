namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// The audio editor's version. It has no project of its own — it lives in the website's library —
/// so its number is kept here.
/// </summary>
/// <remarks>
/// Ben, 10/14/2026: track the audio editor on its own, "because it is going to be growing as the
/// site is further developed." This is the newest released heading of
/// <c>Ben.Web.Services/Changelog/Content/audio-editor.md</c>, and changes only at release, in the same
/// commit that turns "## Unreleased" into "## x.y.z" (ChangelogServiceTests).
/// </remarks>
public static class AudioEditorRelease
{
    public const string Version = "2.0.0";
}
