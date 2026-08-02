using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Public API for community voting on individual cases.
/// </summary>
/// <remarks>
/// Mounted at <c>api/public/cases/{caseId:guid}/votes</c>. Returns
/// <see cref="CaseVoteSummary"/> aggregate counts — voter identity is never exposed.
/// GET is anonymous; POST/DELETE require a valid bearer token.
/// Upsert semantics: a second POST by the same user replaces the existing vote.
/// </remarks>
[ApiController]
[Route("api/public/cases/{caseId:guid}/votes")]
public sealed class PublicCaseVoteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicCaseVoteController(IDbContextFactory<BenDataContext> db) { _db = db; }

    /// <summary>
    /// Returns aggregate vote counts for a public case.
    /// When the caller is authenticated, <c>CurrentUserVote</c> is also populated.
    /// Returns 404 if the case does not exist or is not public.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<CaseVoteSummary>> GetSummary(Guid caseId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.IsPublic
                && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted), ct))
            return NotFound();

        var votes = await db.CaseVotes.AsNoTracking()
            .Where(v => v.CaseId == caseId)
            .ToListAsync(ct);

        var userId = GetCurrentUserId();
        var myVote = userId == Guid.Empty ? null
                   : votes.FirstOrDefault(v => v.VoterAppUserId == userId)?.VoteType;

        return Ok(new CaseVoteSummary(
            CaseId:            caseId,
            ConfirmsCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Confirms),
            DisputesCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Disputes),
            InconclusiveCount: votes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
            TotalVotes:        votes.Count,
            CurrentUserVote:   myVote));
    }

    /// <summary>
    /// Casts or replaces the current user's vote on a public case.
    /// Returns the updated <see cref="CaseVoteSummary"/> so the UI can refresh without a second GET.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<CaseVoteSummary>> CastVote(
        Guid caseId, [FromBody] CastCaseVoteRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.IsPublic
                && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted), ct))
            return NotFound();

        var existing = await db.CaseVotes
            .FirstOrDefaultAsync(v => v.CaseId == caseId && v.VoterAppUserId == userId, ct);

        if (existing is not null)
        {
            existing.VoteType  = request.VoteType;
            existing.Comment   = request.Comment?.Trim();
            existing.DateVoted = DateTime.UtcNow;
        }
        else
        {
            db.CaseVotes.Add(new CaseVote
            {
                Id             = Guid.NewGuid(),
                CaseId         = caseId,
                VoterAppUserId = userId,
                VoteType       = request.VoteType,
                Comment        = request.Comment?.Trim(),
                DateVoted      = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        var votes = await db.CaseVotes.AsNoTracking()
            .Where(v => v.CaseId == caseId)
            .ToListAsync(ct);

        return Ok(new CaseVoteSummary(
            CaseId:            caseId,
            ConfirmsCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Confirms),
            DisputesCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Disputes),
            InconclusiveCount: votes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
            TotalVotes:        votes.Count,
            CurrentUserVote:   request.VoteType));
    }

    /// <summary>
    /// Removes the current user's vote. Returns 204 on success, 404 if no vote exists.
    /// </summary>
    [HttpDelete]
    [Authorize]
    public async Task<IActionResult> RemoveVote(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var vote = await db.CaseVotes
            .FirstOrDefaultAsync(v => v.CaseId == caseId && v.VoterAppUserId == userId, ct);
        if (vote is null) return NotFound();
        db.CaseVotes.Remove(vote);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>Request body for <see cref="PublicCaseVoteController.CastVote"/>.</summary>
/// <param name="VoteType">Confirms / Disputes / Inconclusive.</param>
/// <param name="Comment">Optional supporting comment (stored; not yet surfaced on public endpoints).</param>
public sealed record CastCaseVoteRequest(EvidenceVoteType VoteType, string? Comment);
