using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Tests that change <c>AudioSourceReader.MaximumEditDuration</c>, which is static and so shared
/// by every test in the process.
/// </summary>
/// <remarks>
/// <c>AudioSourceReaderTests</c> lowers it to one second while the edit controller's tests decode
/// two-second files. Run side by side, an edit could be refused for a limit that was never its own
/// (found 10/26/2026, before it had failed anywhere). Not in parallel with anything, because any
/// edit test in any class reads the same value.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AudioEditLimitCollection
{
    public const string Name = "Audio edit limit";
}
