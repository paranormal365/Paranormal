using Ben.Data.Common.Helpers;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Allocating and checking <c>@names</c> against what is already taken.
/// </summary>
/// <remarks>
/// <para><see cref="UserHandle"/> holds the rules; this holds the part that needs the database.
/// Two jobs with different stakes: <see cref="UserHandleService.IsAvailableAsync"/> answers a
/// person's question as they type, and <see cref="UserHandleService.AllocateAsync"/> runs where
/// <b>nobody is present to be asked</b> — an Entra sign-in linking an account, an event magic link,
/// the seeders. That one must always produce something legal, because an account with no handle
/// cannot be mentioned and is invisible to the feed.</para>
///
/// <para>A handle is chosen once and never changed, by Ben's decision, so a bad allocation is
/// permanent.</para>
/// </remarks>
public sealed class UserHandleServiceTests
{
    private static async Task<UserHandleService> WithHandlesAsync(params string[] taken)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var handle in taken)
        {
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = $"{handle}@test.com", NormalizedUserName = $"{handle}@TEST.COM".ToUpperInvariant(),
                Email = $"{handle}@test.com", NormalizedEmail = $"{handle}@TEST.COM".ToUpperInvariant(),
                Handle = handle, DateCreated = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return new UserHandleService(factory);
    }

    // ── Availability ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_free_legal_name_is_available()
    {
        var handles = await WithHandlesAsync("sarahmitchell");

        var (available, reason) = await handles.IsAvailableAsync("jamesthornton");

        Assert.True(available);
        Assert.Null(reason);
    }

    [Fact]
    public async Task A_taken_name_is_refused_and_says_so()
    {
        var handles = await WithHandlesAsync("sarahmitchell");

        var (available, reason) = await handles.IsAvailableAsync("sarahmitchell");

        Assert.False(available);
        Assert.Contains("taken", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Availability_compares_the_normalised_form()
    {
        // "SarahMitchell" and "@SARAHMITCHELL" are the same name. Comparing raw input would let a
        // second account take a name that only differs in case — and then @sarahmitchell in a post
        // would be ambiguous, which is the whole thing handles exist to prevent.
        var handles = await WithHandlesAsync("sarahmitchell");

        Assert.False((await handles.IsAvailableAsync("SarahMitchell")).Available);
        Assert.False((await handles.IsAvailableAsync("@SARAHMITCHELL")).Available);
    }

    [Fact]
    public async Task An_illegal_name_is_refused_before_the_database_is_asked()
    {
        var handles = await WithHandlesAsync();

        var (available, reason) = await handles.IsAvailableAsync("ab");

        Assert.False(available);
        Assert.Contains("at least", reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Allocation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unused_suggestion_is_allocated_as_is()
    {
        var handles = await WithHandlesAsync();

        Assert.Equal("sarahmitchell", await handles.AllocateAsync("Sarah Mitchell", null));
    }

    [Fact]
    public async Task A_collision_is_suffixed_rather_than_refused()
    {
        // There is no human here to be asked for another name, so refusing is not an option.
        var handles = await WithHandlesAsync("sarahmitchell");

        Assert.Equal("sarahmitchell2", await handles.AllocateAsync("Sarah Mitchell", null));
    }

    [Fact]
    public async Task Successive_collisions_keep_counting()
    {
        var handles = await WithHandlesAsync("sarahmitchell", "sarahmitchell2", "sarahmitchell3");

        Assert.Equal("sarahmitchell4", await handles.AllocateAsync("Sarah Mitchell", null));
    }

    [Fact]
    public async Task A_suffix_trims_the_stem_rather_than_overflowing_the_column()
    {
        // A name already at the limit cannot simply have "2" appended: the result would be one
        // character too long for the column and the insert would fail, in a code path where nobody
        // is watching.
        var atLimit = new string('a', UserHandle.MaxLength);
        var handles = await WithHandlesAsync(atLimit);

        var allocated = await handles.AllocateAsync(atLimit, null);

        Assert.True(allocated.Length <= UserHandle.MaxLength, $"'{allocated}' is too long.");
        Assert.NotEqual(atLimit, allocated);
        Assert.True(UserHandle.IsValid(allocated, out var error), error);
    }

    [Fact]
    public async Task Allocation_always_produces_a_legal_name_whatever_it_is_given()
    {
        // These are the inputs the unattended paths actually see: a display name of punctuation, a
        // digits-only name, nothing at all. Every one must still yield something the column and the
        // mention parser accept.
        var handles = await WithHandlesAsync();

        foreach (var (name, email) in new (string?, string?)[]
        {
            ("Sarah Mitchell", null), ("!!!", null), ("2026", null),
            (null, "9@example.com"), (null, null), ("", ""),
        })
        {
            var allocated = await handles.AllocateAsync(name, email);
            Assert.True(UserHandle.IsValid(allocated, out var error),
                $"Allocate({name ?? "null"}, {email ?? "null"}) produced \"{allocated}\": {error}");
        }
    }

    [Fact]
    public async Task Two_accounts_with_nothing_usable_still_get_different_names()
    {
        // Both fall back to the same stem, so the second must be suffixed. Otherwise a batch of
        // imported accounts with no display names would all collide on "user".
        var handles = await WithHandlesAsync("user");

        Assert.Equal("user2", await handles.AllocateAsync(null, null));
    }
}
