using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The shape of the sign-in record, and the reasoning that shape encodes.
/// </summary>
/// <remarks>
/// These are structural assertions rather than behavioural ones: the writing itself happens inside
/// Identity's <c>SignInManager</c> and is proven end to end against a live database (three
/// attempts, two rows — the third being an address matching no account, which never reaches a
/// password check and so has no user to attribute). What a unit test can usefully hold still is
/// the set of columns, because the temptation later will be to add "just an IP address".
/// </remarks>
public sealed class SignInEventTests
{
    [Fact]
    public void A_sign_in_event_records_only_what_a_count_needs()
    {
        var properties = typeof(SignInEvent)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var expected = new[] { "AppUser", "AppUserId", "Id", "Method", "Succeeded", "Utc" }
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(properties.SequenceEqual(expected, StringComparer.Ordinal),
            "The sign-in record has changed shape. It exists to answer 'how many people signed in', "
            + "and every field beyond that turns a counting table into a tracking one — an IP "
            + "address or a user agent brings retention and disclosure questions the dashboard has "
            + "no need to raise. If a new column is genuinely warranted, say why beside the entity "
            + "first.\n"
            + $"  expected: {string.Join(", ", expected)}\n"
            + $"  actual:   {string.Join(", ", properties)}");
    }

    [Fact]
    public void The_user_reference_is_optional()
    {
        // A failed attempt against an address matching no account has nobody to point at. Making
        // this required would mean either inventing a user or dropping the row.
        var appUserId = typeof(SignInEvent).GetProperty(nameof(SignInEvent.AppUserId))!;

        Assert.Equal(typeof(Guid?), appUserId.PropertyType);
    }

    [Fact]
    public void The_password_method_is_named_once()
    {
        // The dashboard separates password sign-ins from Entra ones, which never touch the
        // password endpoint. A literal typed at each write site would eventually disagree.
        Assert.Equal("password", RecordingSignInManager.PasswordMethod);
    }
}
