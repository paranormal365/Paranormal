using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every role flag the app gates a page on must survive a reload.
/// </summary>
/// <remarks>
/// <para><b>The bug this exists to stop.</b> <c>IsModerator</c> was set at sign-in from
/// <c>api/me</c> and then never written to local storage, so the next page load restored a session
/// with the flag silently back to <c>false</c>. The moderation queue then bounced its own
/// moderator to the home page — while the API cheerfully reported <c>isModerator: true</c> — and
/// nothing anywhere said why. Ben hit it as a SuperAdmin on 2026-08-30.</para>
///
/// <para><b>Why a source scan.</b> The failure is not in any method's logic; it is a field missing
/// from three places at once — the record, the write and the restore. A unit test over
/// <c>MainLayout</c> would need a rendered circuit and local storage; reading the file catches the
/// omission at the only point it can be made. Per the standing rule on source-scan guards, this
/// strips comments first, so the words in the note above cannot satisfy it.</para>
/// </remarks>
public sealed class PersistedAuthStateCoversRolesTests
{
    private static string MainLayoutSource()
    {
        var path = Path.Combine(RepoRoot(), "Ben.Web.Website", "Components", "Layout", "MainLayout.razor");
        Assert.True(File.Exists(path), $"MainLayout not found at {path}");
        var raw = File.ReadAllText(path);

        // Comments are prose and would satisfy any token search on their own.
        raw = Regex.Replace(raw, @"@\*[\s\S]*?\*@", string.Empty);
        raw = Regex.Replace(raw, @"(?m)^\s*//.*$", string.Empty);
        return raw;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("IsSuperAdmin")]
    [InlineData("IsAdmin")]
    [InlineData("IsModerator")]
    [InlineData("IsImpersonating")]
    public void Every_role_flag_is_persisted_and_restored(string flag)
    {
        var source = MainLayoutSource();

        // Written: the flag reaches local storage from the token store.
        Assert.True(source.Contains($"TokenStore.{flag},"),
            $"{flag} is never written into PersistedAuthState — a reload will lose it.");

        // Restored: it comes back out again. Without this half the flag is saved and ignored,
        // which is indistinguishable from not saving it.
        Assert.True(source.Contains($"TokenStore.{flag} = state.{flag};"),
            $"{flag} is written but never restored — a reload will still lose it.");

        // Declared on the record, or neither of the above compiles for the right reason.
        Assert.True(Regex.IsMatch(source, $@"bool\s+{flag}\b"),
            $"{flag} is not a field on PersistedAuthState.");
    }
}
