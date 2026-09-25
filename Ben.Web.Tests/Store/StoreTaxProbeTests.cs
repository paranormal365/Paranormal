using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Pretend Stripe is only ever used where all three conditions hold, and the real probe says in
/// words when it cannot ask Stripe (storefront S1.7a).
/// </summary>
public sealed class StoreTaxProbeTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static IConfiguration Config(string? secret, string? allowFake)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = secret,
            [StoreStripeMode.AllowFakeKey] = allowFake,
        }).Build();

    [Theory]
    [InlineData("Development", null, "true", true)]
    [InlineData("Production", null, "true", false)]
    [InlineData("Staging", null, "true", false)]
    [InlineData("Development", "sk_test_123", "true", false)]
    [InlineData("Development", null, "false", false)]
    [InlineData("Development", null, null, false)]
    public void Pretend_stripe_needs_development_no_key_and_the_switch(string env, string? secret, string? allow, bool fake)
        => Assert.Equal(fake, StoreStripeMode.UseFakes(Env(env), Config(secret, allow)));

    [Fact]
    public async Task With_no_key_the_real_probe_says_payment_is_not_set_up()
    {
        var probe = new StripeTaxProbe(Options.Create(new StripeOptions()), NullLogger<StripeTaxProbe>.Instance);

        var readiness = await probe.ProbeAsync();

        Assert.Equal((false, false, "Online payment isn't set up."), (readiness.Active, readiness.HasHeadOffice, readiness.Problem));
        Assert.Empty(readiness.RegisteredStates);
    }
}
