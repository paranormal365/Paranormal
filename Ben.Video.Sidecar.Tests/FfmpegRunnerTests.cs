using System.Text.Json;
using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

public sealed class FfmpegRunnerTests : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("benvideo-runner-test-").FullName;
    private readonly string _argvOutDir = Directory.CreateTempSubdirectory("benvideo-runner-argv-").FullName;
    private readonly FfmpegRunner _runner;

    public FfmpegRunnerTests()
    {
        var locator = new FfmpegLocator(AppContext.BaseDirectory, FakeFfmpegPath.Resolve());
        _runner = new FfmpegRunner(locator);
    }

    public void Dispose()
    {
        Directory.Delete(_workDir, recursive: true);
        Directory.Delete(_argvOutDir, recursive: true);
    }

    private Dictionary<string, string> EnvFor(string mode) =>
        new() { ["FAKE_FFMPEG_MODE"] = mode, ["FAKE_FFMPEG_OUT"] = _argvOutDir };

    [Fact]
    public async Task TryGetVersionAsync_ReturnsTheFakeVersionString()
    {
        var version = await _runner.TryGetVersionAsync();
        Assert.NotNull(version);
        Assert.Contains("FAKE-1.0", version);
    }

    [Fact]
    public async Task RunAsync_OkMode_ReturnsSuccessAndProducesOutputFile()
    {
        var outputPath = Path.Combine(_workDir, "out.mp4");

        var result = await _runner.RunAsync(
            ["-i", "in.mp4", outputPath], _workDir, TimeSpan.FromSeconds(10),
            environmentOverrides: EnvFor("ok"));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task RunAsync_FailMode_ReturnsNonZeroExitCode()
    {
        var result = await _runner.RunAsync(
            ["-i", "in.mp4", Path.Combine(_workDir, "out.mp4")], _workDir, TimeSpan.FromSeconds(10),
            environmentOverrides: EnvFor("fail"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("simulated encode failure", result.StdErr);
    }

    [Fact]
    public async Task RunAsync_HangMode_KilledAfterTimeout()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _runner.RunAsync(
            ["-i", "in.mp4", Path.Combine(_workDir, "out.mp4")], _workDir, TimeSpan.FromSeconds(1),
            environmentOverrides: EnvFor("hang"));

        sw.Stop();
        Assert.True(result.TimedOut);
        // Proves the process was actually killed, not just abandoned — a leaked process would
        // make this test suite (and the real sidecar) accumulate zombie ffmpeg processes forever.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Kill took too long: {sw.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_ArgvIsPassedExactlyAsAList_NeverAShellString()
    {
        // The strongest available proof that argv never passes through a shell: an argument
        // containing shell metacharacters survives byte-for-byte into the child process's own
        // argv, instead of being interpreted (which a `sh -c "..."` invocation would do).
        const string hostileArg = "$(rm -rf /); `echo pwned`; & | ; > out.txt";

        await _runner.RunAsync(
            ["-i", hostileArg, Path.Combine(_workDir, "out.mp4")], _workDir, TimeSpan.FromSeconds(10),
            environmentOverrides: EnvFor("ok"));

        var dumpFile = Directory.GetFiles(_argvOutDir, "*.json").Single();
        var record = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(dumpFile));
        var argsArray = record.GetProperty("args").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Contains(hostileArg, argsArray); // arrived intact — never shell-expanded
        Assert.False(File.Exists(Path.Combine(_workDir, "out.txt"))); // the injected redirect never ran
    }
}
