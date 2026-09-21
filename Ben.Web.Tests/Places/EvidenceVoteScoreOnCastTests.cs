using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Casting a vote answers with the score, not with zero (found 2026-09-21).
/// </summary>
/// <remarks>
/// <para><c>EvidenceVoteSummary.Score</c> documents itself as "computed server-side and rendered
/// as given — never re-derived from the counts, which is how four surfaces end up with four
/// answers". The cast endpoint left the argument off altogether, which did not re-derive it
/// either: it took the record's default of ZERO. So voting answered with a score of 0 however the
/// votes actually stood, and the widget displayed that until something reloaded the page.</para>
///
/// <para>The GET beside it had always been right, which is why nothing noticed. It surfaced while
/// putting the same widget on a place's page, where the per-place figures sit directly above it
/// and the two disagreed in front of the reader.</para>
/// </remarks>
public sealed class EvidenceVoteScoreOnCastTests
{
    private static readonly Guid Voter = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public async Task The_answer_to_a_vote_carries_the_score()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();

        Guid fileId;
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            foreach (var id in new[] { Voter, Other })
                db.AppUsers.Add(new AppUser
                { Id = id, DisplayName = "A voter", DateCreated = DateTime.UtcNow });

            var typeId = Guid.NewGuid();
            db.UploadFileTypes.Add(new UploadFileType
            { Id = typeId, Name = "Evidence", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Voter });

            fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = typeId, AppUserId = Other,
                FileName = "a.jpg", StoredFileName = "a.jpg", ContentType = "image/jpeg",
                FileSize = 1, StoragePath = "x/a.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Other,
            });

            // Somebody else already confirmed it, so a second confirmation must answer +2.
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = Other,
                VoteType = EvidenceVoteType.Confirms, IsPublicVoter = true,
                DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var controller = new EvidenceVoteController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Voter.ToString())], "Bearer")),
                },
            },
        };

        var result = await controller.CastVote(
            fileId, new CastEvidenceVoteRequest(EvidenceVoteType.Confirms, null), default);

        var summary = Assert.IsType<EvidenceVoteSummary>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, summary.ConfirmsCount);
        Assert.Equal(2, summary.TotalVotes);

        // The whole point. This was 0 before the fix, whatever the counts said beside it.
        Assert.Equal(2, summary.Score);
    }
}
