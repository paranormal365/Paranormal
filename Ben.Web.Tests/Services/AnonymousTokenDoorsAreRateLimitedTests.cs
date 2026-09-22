using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A door anybody may call with a guessable-shaped token costs what signing in costs (C4).
/// </summary>
/// <remarks>
/// <para><b>Why a guard and not a note.</b> These doors arrive one at a time — a share link, an
/// event pass, a guest code, an account handover — and each is written by somebody thinking about
/// that feature, not about the set. The handover door carried the auth rate limit from its first
/// commit and the guest-code lookup beside it carried none, which is the shape of a rule kept by
/// memory. Guessing a code is not a practical way in; calling one in a loop from a script is a
/// practical way to cost us money.</para>
///
/// <para>The list is explicit rather than discovered. "Every anonymous action" would sweep in the
/// public pages that exist to be read — the pricing page, a group's own site — and a guard that
/// fires on those gets an exemption list, then gets ignored. These are the doors that take a
/// SECRET from an anonymous caller and answer differently depending on it.</para>
/// </remarks>
public sealed class AnonymousTokenDoorsAreRateLimitedTests
{
    /// <summary>Controller type name → why it is on the list.</summary>
    private static readonly Dictionary<string, string> TokenDoors = new(StringComparer.Ordinal)
    {
        ["PublicInvestigationCodeController"] = "a guest code off a printed sheet",
        ["PublicAccountHandoverController"]   = "a reset code from an account-handover letter",
        ["PublicEventPassController"]         = "an event pass token, rendered as a picture",
    };

    [Fact]
    public void Every_anonymous_token_door_carries_the_auth_rate_limit()
    {
        var api = typeof(Ben.Data.WebApi.Controllers.Public.PublicInvestigationCodeController).Assembly;

        var missing = new List<string>();
        var found = new List<string>();

        foreach (var type in api.GetTypes().Where(t => TokenDoors.ContainsKey(t.Name)))
        {
            found.Add(type.Name);

            // On the class or on any action — the guest-code door limits one action and authorises
            // the rest, which is a different and equally correct shape.
            var onClass = type.GetCustomAttribute<EnableRateLimitingAttribute>() is not null;
            var onAnyAction = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttribute<EnableRateLimitingAttribute>() is not null);

            if (!onClass && !onAnyAction)
                missing.Add($"{type.Name} — {TokenDoors[type.Name]}");
        }

        // The list naming a type that no longer exists would quietly shrink the guard.
        Assert.Equal(
            TokenDoors.Keys.OrderBy(k => k, StringComparer.Ordinal),
            found.OrderBy(k => k, StringComparer.Ordinal));

        Assert.True(missing.Count == 0,
            "these take a secret from an anonymous caller and are not rate limited, so a script "
          + "can walk them for free:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_doors_on_the_list_really_are_anonymous()
    {
        var api = typeof(Ben.Data.WebApi.Controllers.Public.PublicInvestigationCodeController).Assembly;

        // If one of these were made Authorize-only the rate limit would stop mattering and the
        // entry should leave the list — rather than sit here asserting something that is no
        // longer the reason it was added.
        foreach (var type in api.GetTypes().Where(t => TokenDoors.ContainsKey(t.Name)))
        {
            var anonymousSomewhere =
                type.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);

            Assert.True(anonymousSomewhere,
                $"{type.Name} is on the anonymous-token-door list but nothing on it is anonymous");
        }
    }
}
