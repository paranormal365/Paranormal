using Ben.Data.WebApi.Services.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What a session is called on the SuperAdmin list.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16, looking at that list: "I see a bunch of data.json files. I should see one row
/// per session and it should just be the name of the file .ben". Every session uploaded before
/// bundles existed stores a file literally called data.json, so the column repeated it down the
/// whole page and read as a folder of identical files rather than a list of nights.
/// </remarks>
public sealed class SessionFileNameTests
{
    private static readonly DateTime Recorded = new(2026, 9, 16, 10, 32, 0, DateTimeKind.Utc);

    [Fact]
    public void ASessionIsNamedForWhenAndWhereRatherThanForItsStoredFile()
    {
        var name = SessionFileName.For(
            isBundle: false, storedFileName: "data.json", Recorded, "back bedroom, north wall");

        Assert.Equal("2026-09-16 1032 back bedroom, north wall.ben", name);
    }

    /// <summary>
    /// A bundle already carries the name the phone gave it — and one a person may have renamed.
    /// Recomputing would quietly replace what they chose.
    /// </summary>
    [Fact]
    public void ABundleKeepsTheNameItArrivedWith()
    {
        var name = SessionFileName.For(
            isBundle: true, storedFileName: "the night at the Carter house.ben",
            Recorded, "back bedroom");

        Assert.Equal("the night at the Carter house.ben", name);
    }

    /// <summary>
    /// A bundle whose stored name is not a session file name — an older row, or something written
    /// by hand — falls back rather than showing a name that says the wrong thing.
    /// </summary>
    [Fact]
    public void ABundleWithAnUnrecognisableStoredNameIsNamedLikeAnythingElse()
    {
        var name = SessionFileName.For(
            isBundle: true, storedFileName: "data.json", Recorded, "cellar");

        Assert.Equal("2026-09-16 1032 cellar.ben", name);
    }

    [Fact]
    public void ASessionWithNoLabelIsStillCalledSomething()
    {
        Assert.Equal("2026-09-16 1032 Field session.ben",
                     SessionFileName.For(false, "data.json", Recorded, null));
        Assert.Equal("2026-09-16 1032 Field session.ben",
                     SessionFileName.For(false, "data.json", Recorded, "   "));
    }

    /// <summary>The date leads so the list reads in the order the nights happened.</summary>
    [Fact]
    public void NamesSortIntoTheOrderTheyWereRecorded()
    {
        var names = new[]
        {
            SessionFileName.Build(new DateTime(2027, 1, 4, 22, 0, 0, DateTimeKind.Utc), "hall"),
            SessionFileName.Build(new DateTime(2026, 9, 16, 10, 32, 0, DateTimeKind.Utc), "cellar"),
            SessionFileName.Build(new DateTime(2026, 11, 2, 1, 15, 0, DateTimeKind.Utc), "attic"),
        };

        Assert.Equal(
            ["2026-09-16 1032 cellar.ben", "2026-11-02 0115 attic.ben", "2027-01-04 2200 hall.ben"],
            names.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The label is somebody's free text and the name ends up in a download. Same rule as the
    /// phone's, so a session does not change its name by being looked at from the other side.
    /// </summary>
    /// <remarks>
    /// The dots of "../.." survive as words, which is deliberate rather than overlooked: what
    /// makes a path is the separator, and "&#46;&#46; &#46;&#46; etc passwd.ben" is a file name in one
    /// directory. Dropping them would also mean this and the phone's rule quietly disagreeing about
    /// what the same session is called, which is worse than an odd-looking name.
    /// </remarks>
    [Theory]
    [InlineData("../../etc/passwd", ".. .. etc passwd")]
    [InlineData("the \"cellar\"", "the cellar")]
    [InlineData("C:\\rooms\\attic", "C rooms attic")]
    public void ALabelCannotMakeTheNameIntoAPath(string label, string expected)
    {
        var name = SessionFileName.Build(Recorded, label);

        Assert.Equal($"2026-09-16 1032 {expected}.ben", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
    }

    [Fact]
    public void AnEnormousLabelIsCutRatherThanCarried()
    {
        var name = SessionFileName.Build(Recorded, string.Concat(Enumerable.Repeat("cellar ", 40)));
        Assert.True(name.Length < 100, $"the name was {name.Length} characters");
    }
}
