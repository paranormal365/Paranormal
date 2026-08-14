using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages votes on <c>UploadFile</c> records.
///
/// Business rules enforced here:
/// • One vote per (user, file) — unique index in DB; upsert avoids duplicate inserts.
/// • A user can update their score at any time (PUT).
/// • A user can remove their vote at any time (DELETE).
/// • Anyone authenticated can read the vote summary.
/// </summary>
[ApiController]
[Route("api/upload-files/{fileId:guid}/votes")]
[Authorize]
public sealed class UploadFileVoteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public UploadFileVoteController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>Returns the aggregated vote summary for a file, including the caller's vote if present.</summary>
    [HttpGet]
    public async Task<ActionResult<UploadFileVoteSummary>> GetSummary(Guid fileId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var votes = await db.UploadFileVotes.AsNoTracking()
            .Where(v => v.UploadFileId == fileId)
            .ToListAsync(ct);

        var userId    = GetCurrentUserId();
        var userVote  = userId != Guid.Empty ? votes.FirstOrDefault(v => v.AppUserId == userId) : null;

        return Ok(new UploadFileVoteSummary(
            UploadFileId:  fileId,
            UpvoteCount:   votes.Count(v => v.Score > 0),
            DownvoteCount: votes.Count(v => v.Score < 0),
            TotalScore:    votes.Sum(v => v.Score),
            TotalVotes:    votes.Count,
            UserScore:     userVote?.Score));
    }

    /// <summary>
    /// Creates or updates the calling user's vote on the file (upsert).
    /// Returns 201 on first vote, 200 on update.
    /// </summary>
    [HttpPut("my-vote")]
    public async Task<ActionResult<UploadFileVoteRecord>> UpsertMyVote(
        Guid fileId, [FromBody] UpsertVoteRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var voteBefore = await db.UploadFileVotes.AsNoTracking()
            .FirstOrDefaultAsync(v => v.UploadFileId == fileId && v.AppUserId == userId, ct);

        var existing = await db.UploadFileVotes
            .FirstOrDefaultAsync(v => v.UploadFileId == fileId && v.AppUserId == userId, ct);

        if (existing is not null)
        {
            existing.Score       = request.Score;
            existing.DateUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileVote), existing.Id, voteBefore!, existing, userId, AppSources.WebApi, ct));
            return Ok(_mapper.Map<UploadFileVoteRecord>(existing));
        }

        // Verify the file exists before creating a vote
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct))
            return NotFound("Upload file not found.");

        var vote = new UploadFileVote
        {
            Id           = Guid.NewGuid(),
            UploadFileId = fileId,
            AppUserId    = userId,
            Score        = request.Score,
            DateCreated  = DateTime.UtcNow,
        };
        db.UploadFileVotes.Add(vote);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race against a concurrent first vote from the same user — this is an
            // upsert endpoint, so fall through to updating the row that won instead of erroring.
            db.Entry(vote).State = EntityState.Detached;
            var winner = await db.UploadFileVotes
                .FirstAsync(v => v.UploadFileId == fileId && v.AppUserId == userId, ct);
            winner.Score       = request.Score;
            winner.DateUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileVote), winner.Id, winner, winner, userId, AppSources.WebApi, ct));
            return Ok(_mapper.Map<UploadFileVoteRecord>(winner));
        }
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileVote), vote.Id, vote, userId, AppSources.WebApi, ct));

        return CreatedAtAction(nameof(GetSummary), new { fileId },
            _mapper.Map<UploadFileVoteRecord>(vote));
    }

    /// <summary>Removes the calling user's vote. Returns 204 whether or not a vote existed.</summary>
    [HttpDelete("my-vote")]
    public async Task<IActionResult> RemoveMyVote(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var vote = await db.UploadFileVotes
            .FirstOrDefaultAsync(v => v.UploadFileId == fileId && v.AppUserId == userId, ct);

        if (vote is not null)
        {
            db.UploadFileVotes.Remove(vote);
            await db.SaveChangesAsync(ct);
            _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFileVote), vote.Id, vote, userId, AppSources.WebApi, ct));
        }

        return NoContent();
    }

}
