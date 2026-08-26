using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group's review of a client request it has been offered: the full submission, the
/// materials, and the members' vote on taking it on.
/// </summary>
/// <remarks>
/// <para><b>The flow (Ben, 2026-08-26):</b> marking an application Under Review messages the
/// group's eligible members with a link here; each member sees everything the client submitted —
/// text, photos, any file type — and votes. The vote is advisory: whoever holds the accept grant
/// decides, with the tally in front of them, and any group who accepts first wins.</para>
///
/// <para><b>Gate: <c>Case.Read</c>,</b> the same grant that shows the pending-requests list —
/// reviewing a request is reading prospective case material. The application row is the other
/// half of the door: a group with no live application for this request sees nothing, however
/// grantful its members are.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/request-review/{clientRequestId:guid}")]
[Authorize]
public sealed class ClientRequestReviewController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    public ClientRequestReviewController(
        IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _security = security; }

    private async Task<bool> MayReviewAsync(Guid orgId, CancellationToken ct)
        => User.IsInRole(RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.Case, OrganizationSecurityAction.Read, ct);

    /// <summary>A live application: one the review page should still open for.</summary>
    /// <remarks>Rejected and Cancelled are dead — a group that declined, or lost the race, has
    /// no business re-reading someone's home photos. Accepted stays live so the winning group can
    /// still see the ballot that led to the decision.</remarks>
    private static Task<ClientRequestOrganization?> LiveApplicationAsync(
        BenDataContext db, Guid orgId, Guid clientRequestId, CancellationToken ct)
        => db.ClientRequestOrganizations
            .FirstOrDefaultAsync(a => a.ClientRequestId == clientRequestId
                                   && a.OrganizationId == orgId
                                   && a.Status != ClientOrgRequestStatus.Rejected
                                   && a.Status != ClientOrgRequestStatus.Cancelled, ct)!;

    /// <summary>Everything a reviewer needs on one screen: submission, materials, ballot.</summary>
    [HttpGet]
    public async Task<ActionResult<RequestReviewDetail>> Get(
        Guid orgId, Guid clientRequestId, CancellationToken ct)
    {
        if (!await MayReviewAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var application = await LiveApplicationAsync(db, orgId, clientRequestId, ct);
        if (application is null) return NotFound();

        var request = await db.ClientRequests.AsNoTracking()
            .Where(r => r.Id == clientRequestId)
            .Select(r => new
            {
                r.DateCreated, r.Description, r.Gender, r.BirthYear,
                r.StreetAddress1, r.StreetAddress2, r.City, r.State, r.ZipCode, r.Country,
                Files = r.Files.OrderBy(f => f.DateCreated).Select(f => new RequestReviewFile(
                    f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (request is null) return NotFound();

        var votes = await db.ClientRequestReviewVotes.AsNoTracking()
            .Where(v => v.ClientRequestOrganizationId == application.Id)
            .OrderBy(v => v.DateVoted)
            .Select(v => new RequestReviewVoteRecord(
                v.VoterAppUserId,
                v.VoterAppUser.DisplayName ?? "Member",
                v.InFavor, v.Comment, v.DateVoted))
            .ToListAsync(ct);

        return Ok(new RequestReviewDetail(
            clientRequestId,
            application.Status,
            request.DateCreated,
            request.Description,
            request.Gender,
            request.BirthYear,
            request.StreetAddress1, request.StreetAddress2,
            request.City, request.State, request.ZipCode, request.Country,
            request.Files,
            votes,
            votes.FirstOrDefault(v => v.VoterAppUserId == userId)));
    }

    /// <summary>Cast — or change — this member's vote. One ballot per member.</summary>
    [HttpPost("vote")]
    public async Task<ActionResult<RequestReviewVoteRecord>> CastVote(
        Guid orgId, Guid clientRequestId, [FromBody] CastReviewVoteRequest body, CancellationToken ct)
    {
        if (!await MayReviewAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var application = await LiveApplicationAsync(db, orgId, clientRequestId, ct);
        if (application is null) return NotFound();
        if (application.Status != ClientOrgRequestStatus.UnderReview)
            return BadRequest("Voting opens when the request is marked Under Review.");

        var vote = await db.ClientRequestReviewVotes
            .FirstOrDefaultAsync(v => v.ClientRequestOrganizationId == application.Id
                                   && v.VoterAppUserId == userId, ct);
        if (vote is null)
        {
            vote = new ClientRequestReviewVote
            {
                Id = Guid.NewGuid(),
                ClientRequestOrganizationId = application.Id,
                VoterAppUserId = userId,
            };
            db.ClientRequestReviewVotes.Add(vote);
        }
        vote.InFavor   = body.InFavor;
        vote.Comment   = string.IsNullOrWhiteSpace(body.Comment) ? null : body.Comment.Trim();
        vote.DateVoted = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var name = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(new RequestReviewVoteRecord(userId, name ?? "Member", vote.InFavor, vote.Comment, vote.DateVoted));
    }
}

public sealed record CastReviewVoteRequest(bool InFavor, string? Comment);

public sealed record RequestReviewFile(Guid UploadFileId, string FileName, string ContentType, long FileSize);

public sealed record RequestReviewVoteRecord(
    Guid VoterAppUserId, string VoterDisplayName, bool InFavor, string? Comment, DateTime DateVoted);

/// <summary>The client's submission as the reviewing group sees it — no name, ever: the
/// client's identity deliberately stays off org-facing records until they meet their group.</summary>
public sealed record RequestReviewDetail(
    Guid ClientRequestId,
    ClientOrgRequestStatus ApplicationStatus,
    DateTime DateSubmitted,
    string? Description,
    ClientGender Gender,
    int? BirthYear,
    string StreetAddress1, string? StreetAddress2,
    string City, string State, string ZipCode, string Country,
    IReadOnlyList<RequestReviewFile> Files,
    IReadOnlyList<RequestReviewVoteRecord> Votes,
    RequestReviewVoteRecord? MyVote);
