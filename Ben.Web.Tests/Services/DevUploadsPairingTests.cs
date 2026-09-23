using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A development API pointed at a scratch database keeps that database's files beside it (W14).
/// </summary>
/// <remarks>
/// Found 2026-09-23: photos uploaded to <c>IsHauntedDb_e2e_block1</c> by a hand-started API sat in
/// the shared <c>.uploads</c>, so every later run against that database served their rows as
/// broken images. No credentials here — only the database name is read from each string.
/// </remarks>
public sealed class DevUploadsPairingTests
{
    private const string Shared = "/Users/dev/Source/Ben/.uploads";

    /// <summary>
    /// The folder beside <see cref="Shared"/>, spelled the way this platform's <c>Path</c> spells it —
    /// "/Users/dev/Source/Ben/.uploads-x" on the Mac, "\Users\dev\...\.uploads-x" on Windows.
    /// </summary>
    private static string Beside(string folder) => Path.Combine(Path.GetDirectoryName(Shared)!, folder);

    [Theory]
    [InlineData("Server=db;Database=IsHauntedDb_e2e_block1;Integrated Security=true", ".uploads-IsHauntedDb_e2e_block1")]
    [InlineData("Server=db;Initial Catalog=IsHauntedDb_player;Integrated Security=true", ".uploads-IsHauntedDb_player")]
    public void A_scratch_database_gets_the_folder_named_after_it(string connection, string expectedFolder)
        => Assert.Equal(Beside(expectedFolder), DevUploadsPairing.PairedRoot(connection, Shared + "/"));

    [Fact]
    public void The_default_database_keeps_the_shared_folder()
        => Assert.Null(DevUploadsPairing.PairedRoot("Server=db;Database=IsHauntedDb;Integrated Security=true", Shared));

    /// <summary>A root somebody set on purpose — run-e2e.sh's own, or anything else — is left alone.</summary>
    [Theory]
    [InlineData("/Users/dev/Source/Ben/.uploads-IsHauntedDb_e2e")]
    [InlineData("/srv/ishaunted/uploads")]
    public void A_root_set_on_purpose_is_left_alone(string root)
        => Assert.Null(DevUploadsPairing.PairedRoot("Server=db;Database=IsHauntedDb_e2e;Integrated Security=true", root));

    [Fact]
    public void Applying_it_changes_what_storage_reads_and_says_so()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BenDbConnectionString"] = "Server=db;Database=IsHauntedDb_e2e_x;Integrated Security=true",
            ["FileStorage:RootPath"] = Shared,
        }).Build();
        var log = new StringWriter();

        DevUploadsPairing.Apply(config, log);

        Assert.Equal(Beside(".uploads-IsHauntedDb_e2e_x"), config["FileStorage:RootPath"]);
        Assert.Contains(".uploads-IsHauntedDb_e2e_x", log.ToString());
    }
}
