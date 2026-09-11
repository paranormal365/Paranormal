using Ben.Data.Common;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A key that cannot be read says so, and says which of the two reasons it was.
/// </summary>
/// <remarks>
/// The API refused to start on 2026-09-11 with "names a file that does not exist", naming a key
/// that was there all along; the pool identity simply could not read the locked folder it sits in.
/// File.Exists cannot tell those apart, so the message accused the wrong thing.
/// </remarks>
public class PrivateKeyFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("benkeys").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_path_means_the_feature_is_off_rather_than_broken(string? path)
        => Assert.Equal(string.Empty, PrivateKeyFile.ReadOrEmpty(path, "Maps:PrivateKeyPath"));

    [Fact]
    public void A_readable_key_comes_back_whole()
    {
        var path = Path.Combine(_dir, "AuthKey_TEST.p8");
        File.WriteAllText(path, "-----BEGIN PRIVATE KEY-----\nnot a real key\n-----END PRIVATE KEY-----");

        Assert.Contains("BEGIN PRIVATE KEY", PrivateKeyFile.ReadOrEmpty(path, "Maps:PrivateKeyPath"));
    }

    [Fact]
    public void A_missing_file_says_it_is_not_there_and_names_the_setting()
    {
        var path = Path.Combine(_dir, "nothing-here.p8");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PrivateKeyFile.ReadOrEmpty(path, "Maps:PrivateKeyPath"));

        Assert.Contains("Maps:PrivateKeyPath", ex.Message);
        Assert.Contains("not there", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void A_missing_folder_is_reported_as_a_folder_not_as_a_file()
    {
        var path = Path.Combine(_dir, "no-such-folder", "AuthKey_TEST.p8");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PrivateKeyFile.ReadOrEmpty(path, "Apple:PrivateKeyPath"));

        Assert.Contains("Apple:PrivateKeyPath", ex.Message);
        Assert.Contains("folder", ex.Message);
    }

    /// <summary>
    /// The case that cost the outage. A directory opened as a file raises UnauthorizedAccessException
    /// on every platform, which is the same door the locked deploy folder came through — so the
    /// wording is exercised here without needing an ACL a test cannot portably set.
    /// </summary>
    [Fact]
    public void A_key_that_cannot_be_read_says_so_and_names_who_was_asking()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PrivateKeyFile.ReadOrEmpty(_dir, "Maps:PrivateKeyPath"));

        Assert.Contains("Maps:PrivateKeyPath", ex.Message);
        Assert.Contains("may not read", ex.Message);
        Assert.Contains(Environment.UserName, ex.Message);
        Assert.Contains("grant that identity read on the file", ex.Message);
    }
}
