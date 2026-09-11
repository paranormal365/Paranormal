using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Voting in a poll attached to a message (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// Creating one happens with the message it belongs to — a poll with no message is a question
/// nobody can see, so there is no endpoint that makes one on its own. This is where people answer.
/// </remarks>
[ApiController]
[Route("api/polls/{pollId:guid}")]
public sealed class MessagePollController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MessagePollController(IDbContextFactory<BenDataContext> db) { _db = db; }

    /// <summary>How a poll stands, with this reader's own answer when they have one.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<MessagePollRecord>> Get(Guid pollId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var record = await ReadAsync(db, pollId, GetCurrentUserId(), ct);
        return record is null ? NotFound() : Ok(record);
    }

    /// <summary>
    /// Answers it, replacing whatever this person chose before.
    /// </summary>
    /// <remarks>
    /// <para>Replace rather than add: changing your mind is the ordinary case, and an endpoint
    /// that only ever added would make a second press count twice.</para>
    ///
    /// <para>An empty list is how a vote is taken back, which matches every other vote on this
    /// site — pressing your own answer again clears it.</para>
    /// </remarks>
    [HttpPost("votes")]
    [Authorize]
    public async Task<ActionResult<MessagePollRecord>> Vote(
        Guid pollId, [FromBody] CastPollVoteRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var poll = await db.MessagePolls
            .Include(p => p.Options)
            .FirstOrDefaultAsync(p => p.Id == pollId, ct);
        if (poll is null) return NotFound();

        // The clock is the server's. A closed poll that a stale page still shows as open must be
        // refused here, or the last word goes to whoever left a tab open.
        if (poll.ClosesAtUtc is { } closes && closes <= DateTime.UtcNow)
            return BadRequest("That poll has closed.");

        var chosen = (request.OptionIds ?? []).Distinct().ToList();

        if (chosen.Any(id => poll.Options.All(o => o.Id != id)))
            return BadRequest("That isn't one of the answers.");

        if (!poll.AllowMultiple && chosen.Count > 1)
            return BadRequest("This poll takes one answer.");

        var existing = await db.MessagePollVotes
            .Where(v => v.MessagePollId == pollId && v.AppUserId == userId)
            .ToListAsync(ct);
        db.MessagePollVotes.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var optionId in chosen)
        {
            db.MessagePollVotes.Add(new MessagePollVote
            {
                Id = Guid.NewGuid(),
                MessagePollId = pollId,
                MessagePollOptionId = optionId,
                AppUserId = userId,
                DateCreated = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(await ReadAsync(db, pollId, userId, ct));
    }

    /// <summary>
    /// One poll, counted.
    /// </summary>
    /// <remarks>
    /// The total is DISTINCT people, not rows: a poll that takes several answers has more rows
    /// than voters, and "60% of 14 votes" over rows is a percentage of nothing anybody recognises.
    /// </remarks>
    internal static async Task<MessagePollRecord?> ReadAsync(
        BenDataContext db, Guid pollId, Guid readerId, CancellationToken ct)
    {
        var poll = await db.MessagePolls.AsNoTracking()
            .Where(p => p.Id == pollId)
            .Select(p => new { p.Id, p.Question, p.AllowMultiple, p.ClosesAtUtc })
            .FirstOrDefaultAsync(ct);
        if (poll is null) return null;

        var options = await db.MessagePollOptions.AsNoTracking()
            .Where(o => o.MessagePollId == pollId)
            .OrderBy(o => o.SortOrder)
            .Select(o => new MessagePollOptionRecord(
                o.Id, o.Text, o.Votes.Count))
            .ToListAsync(ct);

        var voters = await db.MessagePollVotes.AsNoTracking()
            .Where(v => v.MessagePollId == pollId)
            .Select(v => v.AppUserId)
            .Distinct()
            .CountAsync(ct);

        var mine = readerId == Guid.Empty
            ? []
            : await db.MessagePollVotes.AsNoTracking()
                .Where(v => v.MessagePollId == pollId && v.AppUserId == readerId)
                .Select(v => v.MessagePollOptionId)
                .ToListAsync(ct);

        return new MessagePollRecord(
            poll.Id, poll.Question, options, voters, mine, poll.AllowMultiple, poll.ClosesAtUtc,
            poll.ClosesAtUtc is { } c && c <= DateTime.UtcNow);
    }
}
