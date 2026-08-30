using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The key ring has to land somewhere that survives a restart, or every token dies with the process.
/// </summary>
/// <remarks>
/// Nothing called <c>AddDataProtection()</c>, so the key ring was regenerated per process and every
/// restart of the API silently invalidated every access and refresh token ever issued. These tests
/// pin the decision about *where* it goes, which is the part that can be got wrong quietly: a path
/// under the content root would be wiped by the next publish, and an empty <c>RootPath</c> — the
/// tracked default in appsettings — would put it in the process working directory.
/// </remarks>
public sealed class DataProtectionSetupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void An_explicit_key_ring_path_wins()
    {
        var path = DataProtectionSetup.ResolveKeyRingPath(Config(
            ("DataProtection:KeyRingPath", "/var/ishaunted/keys"),
            ("FileStorage:RootPath", "/var/ishaunted/uploads")));

        Assert.Equal(Path.GetFullPath("/var/ishaunted/keys"), path);
    }

    [Fact]
    public void Otherwise_it_sits_beside_the_upload_root()
    {
        // Chosen because FileStorage:RootPath is already required to survive a deploy and to be
        // backed up — the key ring inherits both instead of needing them remembered separately.
        var path = DataProtectionSetup.ResolveKeyRingPath(Config(
            ("FileStorage:RootPath", "/var/ishaunted/uploads")));

        Assert.Equal(
            Path.GetFullPath(Path.Combine("/var/ishaunted/uploads", DataProtectionSetup.KeyRingFolderName)),
            path);
    }

    [Fact]
    public void An_empty_upload_root_is_unset_not_the_working_directory()
    {
        // "RootPath": "" is what appsettings.json ships. Treating it as a path would silently write
        // the key ring wherever the process happened to start.
        Assert.Null(DataProtectionSetup.ResolveKeyRingPath(Config(("FileStorage:RootPath", ""))));
        Assert.Null(DataProtectionSetup.ResolveKeyRingPath(Config(("FileStorage:RootPath", "   "))));
        Assert.Null(DataProtectionSetup.ResolveKeyRingPath(Config(("DataProtection:KeyRingPath", ""))));
    }

    [Fact]
    public void Nothing_configured_resolves_to_nothing_rather_than_a_guess()
    {
        Assert.Null(DataProtectionSetup.ResolveKeyRingPath(Config()));
    }

    [Fact]
    public void The_application_name_is_pinned_rather_than_derived_from_the_assembly()
    {
        // Renaming the project must not read as a different application and sign everybody out.
        Assert.Equal("IsHaunted.Api", DataProtectionSetup.ApplicationName);
    }

    [Fact]
    public void A_payload_protected_by_one_process_can_be_read_by_the_next()
    {
        // The whole fix in one assertion. Two service providers over one directory stand in for two
        // runs of the API: before this change the second could not read what the first wrote, which
        // is why every bearer token died on restart. Run this against a Program.cs with no
        // AddBenDataProtection and the second Unprotect throws CryptographicException.
        var directory = Path.Combine(Path.GetTempPath(), "ben-dp-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Protected()
            {
                var services = new ServiceCollection();
                services.AddBenDataProtection(
                    Config(("DataProtection:KeyRingPath", directory)), Serilog.Log.Logger);
                return services.BuildServiceProvider()
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("test").Protect("a bearer token");
            }

            var payload = Protected();

            var second = new ServiceCollection();
            second.AddBenDataProtection(Config(("DataProtection:KeyRingPath", directory)), Serilog.Log.Logger);
            var recovered = second.BuildServiceProvider()
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test").Unprotect(payload);

            Assert.Equal("a bearer token", recovered);
            Assert.NotEmpty(Directory.GetFiles(directory, "*.xml"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void With_nowhere_to_put_the_keys_it_registers_nothing_rather_than_pretending()
    {
        // Deliberately not a throw: refusing to start would take down a dev run over something that
        // only matters across restarts. The registration warns instead — see DataProtectionSetup.
        var services = new ServiceCollection();
        services.AddBenDataProtection(Config(), Serilog.Log.Logger);

        Assert.Null(services.BuildServiceProvider().GetService<IDataProtectionProvider>());
    }
}
