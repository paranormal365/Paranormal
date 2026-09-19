using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// One row per Microsoft visit — not per request, and not once per restart.
/// </summary>
public sealed class EntraSignInRecorderTests
{
    private static (EntraSignInRecorder Recorder, IDbContextFactory<Ben.Data.Source.Context.BenDataContext> Db, MemoryCache Cache) Build()
    {
        var db = TestDbFactory.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new EntraSignInRecorder(db, cache, NullLogger<EntraSignInRecorder>.Instance), db, cache);
    }

    private static async Task<List<SignInEvent>> EntraRowsAsync(
        IDbContextFactory<Ben.Data.Source.Context.BenDataContext> factory, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.SignInEvents
            .Where(e => e.AppUserId == userId && e.Method == RecordingSignInManager.EntraMethod)
            .ToListAsync();
    }

    [Fact]
    public async Task The_first_request_records_an_arrival()
    {
        var (recorder, db, _) = Build();
        var user = Guid.NewGuid();

        await recorder.NoteRequestAsync(user);

        var rows = await EntraRowsAsync(db, user);
        Assert.Single(rows);
        Assert.True(rows[0].Succeeded);
    }

    /// <summary>
    /// The case this exists for: a token arrives on every page change and must not be counted.
    /// </summary>
    [Fact]
    public async Task A_page_full_of_requests_is_still_one_arrival()
    {
        var (recorder, db, _) = Build();
        var user = Guid.NewGuid();

        for (var i = 0; i < 25; i++) await recorder.NoteRequestAsync(user);

        Assert.Single(await EntraRowsAsync(db, user));
    }

    /// <summary>
    /// A deploy empties the cache. Without the database check behind it, every person still
    /// holding a token would be counted as arriving again — and a week of deploys would read as a
    /// week of visitors.
    /// </summary>
    [Fact]
    public async Task A_restart_does_not_invent_an_arrival()
    {
        var (recorder, db, _) = Build();
        var user = Guid.NewGuid();
        await recorder.NoteRequestAsync(user);

        // The same database, a new process: new recorder, empty cache.
        var afterRestart = new EntraSignInRecorder(
            db, new MemoryCache(new MemoryCacheOptions()), NullLogger<EntraSignInRecorder>.Instance);
        await afterRestart.NoteRequestAsync(user);

        Assert.Single(await EntraRowsAsync(db, user));
    }

    [Fact]
    public async Task Coming_back_the_next_day_is_a_second_arrival()
    {
        var (recorder, db, cache) = Build();
        var user = Guid.NewGuid();
        await recorder.NoteRequestAsync(user);

        // Age the row past the window, and drop the cache entry that is standing in for it —
        // which is exactly what happens when the entry expires.
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var row = await ctx.SignInEvents.FirstAsync(e => e.AppUserId == user);
            row.Utc = DateTime.UtcNow - EntraSignInSessions.Visit - TimeSpan.FromMinutes(1);
            await ctx.SaveChangesAsync();
        }
        cache.Remove($"entra-visit:{user}");

        await recorder.NoteRequestAsync(user);

        Assert.Equal(2, (await EntraRowsAsync(db, user)).Count);
    }

    /// <summary>
    /// Two people are two visitors, however close together they arrive.
    /// </summary>
    [Fact]
    public async Task One_persons_visit_does_not_cover_anothers()
    {
        var (recorder, db, _) = Build();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();

        await recorder.NoteRequestAsync(one);
        await recorder.NoteRequestAsync(two);

        Assert.Single(await EntraRowsAsync(db, one));
        Assert.Single(await EntraRowsAsync(db, two));
    }

    /// <summary>
    /// A password sign-in an hour ago is not a Microsoft visit, and must not suppress one.
    /// </summary>
    [Fact]
    public async Task Another_methods_sign_in_does_not_count_as_this_one()
    {
        var (recorder, db, _) = Build();
        var user = Guid.NewGuid();

        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.SignInEvents.Add(new SignInEvent
            {
                Id = Guid.NewGuid(), AppUserId = user, Utc = DateTime.UtcNow.AddHours(-1),
                Succeeded = true, Method = RecordingSignInManager.PasswordMethod,
            });
            await ctx.SaveChangesAsync();
        }

        await recorder.NoteRequestAsync(user);

        Assert.Single(await EntraRowsAsync(db, user));
    }
}
