using Ben.Video.Sidecar.Security;

namespace Ben.Video.Sidecar.Tests;

public sealed class PairingTokenStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("benvideo-token-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LoadOrCreate_NoExistingFile_GeneratesAndReportsCreated()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        Assert.True(store.WasJustCreated);
        Assert.NotNull(store.PlaintextOnFirstRun);
        Assert.True(File.Exists(Path.Combine(_dir, "pairing-token")));
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_LoadsWithoutExposingPlaintext()
    {
        var first = new PairingTokenStore(_dir);
        first.LoadOrCreate();
        var originalToken = first.PlaintextOnFirstRun!;

        var second = new PairingTokenStore(_dir);
        second.LoadOrCreate();

        Assert.False(second.WasJustCreated);
        Assert.Null(second.PlaintextOnFirstRun);
        Assert.True(second.Matches(originalToken)); // the loaded token still matches the original
    }

    [Fact]
    public void Matches_CorrectToken_ReturnsTrue()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        Assert.True(store.Matches(store.PlaintextOnFirstRun));
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("")]
    [InlineData(null)]
    public void Matches_WrongOrMissingToken_ReturnsFalse(string? presented)
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        Assert.False(store.Matches(presented));
    }

    [Fact]
    public void Generate_Twice_InvalidatesThePreviousToken()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();
        var oldToken = store.PlaintextOnFirstRun!;

        store.Generate();
        var newToken = store.PlaintextOnFirstRun!;

        Assert.NotEqual(oldToken, newToken);
        Assert.False(store.Matches(oldToken));
        Assert.True(store.Matches(newToken));
    }

    [Fact]
    public void Generate_UnixFileMode_IsUserReadWriteOnly()
    {
        if (OperatingSystem.IsWindows()) return; // Unix file modes don't apply on Windows.

        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        var mode = File.GetUnixFileMode(Path.Combine(_dir, "pairing-token"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
