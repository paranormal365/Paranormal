using Ben.Data.WebApi.Client.Auth;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Which API a build points at, and which addresses it must refuse to keep.</summary>
public sealed class ApiEnvironmentTests
{
    /// <summary>
    /// Development binds IPv4 explicitly, not <c>localhost</c>.
    /// </summary>
    /// <remarks>
    /// .NET on macOS crashes in its IPv6 accept path, so every dev host in this repo listens on
    /// 127.0.0.1 only (item 187). A client asking for <c>localhost</c> tries <c>::1</c> first and
    /// is refused by a server that is running perfectly well.
    /// </remarks>
    [Fact]
    public void The_development_api_is_addressed_by_ipv4()
    {
        Assert.Equal("127.0.0.1", ApiEnvironment.Dev.BaseUrl.Host);
        Assert.Equal(5252, ApiEnvironment.Dev.BaseUrl.Port);
    }

    /// <summary>
    /// Production keeps its sub-path.
    /// </summary>
    /// <remarks>
    /// The API is an IIS sub-application at <c>/webapi</c>. Losing that path turns every call into
    /// a request against the website, which answers a 404 or an HTML page — and reads to a person
    /// as "the app is broken" rather than "it is pointed at the wrong place".
    /// </remarks>
    [Fact]
    public void Production_keeps_its_sub_path()
    {
        Assert.Equal("/webapi", ApiEnvironment.Production.BaseUrl.AbsolutePath.TrimEnd('/'));
        Assert.Equal("ishaunted.com", ApiEnvironment.Production.BaseUrl.Host);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5252", false)]
    [InlineData("http://localhost:5252", false)]
    [InlineData("http://[::1]:5252", false)]
    [InlineData("http://bens-mac.local:5252", false)]
    [InlineData("http://10.0.0.4:5252", false)]
    [InlineData("http://192.168.1.71:5252", false)]
    [InlineData("http://172.16.0.9:5252", false)]
    [InlineData("http://172.31.255.1:5252", false)]
    [InlineData("http://169.254.1.1:5252", false)]
    [InlineData("https://ishaunted.com/webapi", true)]
    [InlineData("https://uat.ishaunted.com/webapi", true)]
    [InlineData("http://8.8.8.8", true)]
    public void An_address_knows_whether_it_could_work_elsewhere(string url, bool reachable)
        => Assert.Equal(reachable, new ApiEnvironment("t", new Uri(url)).IsReachableOffDevelopmentMachine);

    /// <summary>
    /// 172.32 is public. Treating all of 172 as private is the usual way this check is written wrong.
    /// </summary>
    [Theory]
    [InlineData("http://172.15.0.1", true)]
    [InlineData("http://172.32.0.1", true)]
    public void The_172_block_stops_where_the_rfc_says(string url, bool reachable)
        => Assert.Equal(reachable, new ApiEnvironment("t", new Uri(url)).IsReachableOffDevelopmentMachine);
}
