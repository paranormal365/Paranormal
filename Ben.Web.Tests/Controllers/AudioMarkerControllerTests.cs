using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="AudioMarkerController"/> — verifies CRUD operations,
/// ordering by time, and proper validation of parent file existence.
/// </summary>
public class AudioMarkerControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>
    /// Projects every field, in one place used by both mapper overloads. A stand-in mapper that
    /// silently drops a field turns an assertion about that field into a test of the stand-in, so
    /// this has to stay in step with <see cref="AudioMarkerRecord"/>.
    /// </summary>
    private static AudioMarkerRecord ToRecord(AudioMarker e) => new()
    {
        Id                     = e.Id,
        UploadFileId           = e.UploadFileId,
        TimeSeconds            = e.TimeSeconds,
        EndSeconds             = e.EndSeconds,
        Label                  = e.Label,
        ConfidenceLevel        = e.ConfidenceLevel,
        Note                   = e.Note,
        IsAutoDetected         = e.IsAutoDetected,
        DetectionScore         = e.DetectionScore,
        ReviewStatus           = e.ReviewStatus,
        LinkedClipUploadFileId = e.LinkedClipUploadFileId,
        DateCreated            = e.DateCreated,
        DateUpdated            = e.DateUpdated,
        CreatedByAppUserId     = e.CreatedByAppUserId,
        UpdatedByAppUserId     = e.UpdatedByAppUserId,
    };

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<AudioMarkerRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is AudioMarker e ? ToRecord(e) : new AudioMarkerRecord { Label = "" });
        m.Setup(x => x.Map<IEnumerable<AudioMarkerRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<AudioMarker> list ? list.Select(ToRecord) : []);
        return m.Object;
    }

    private static AudioMarkerController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid? userId = null,
        IFileStorageService? storage = null)
    {
        var ctrl = new AudioMarkerController(
            factory, CreateMapper(), new Mock<IAuditLogService>().Object, storage ?? new Mock<IFileStorageService>().Object);
        var claims = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
              ], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return ctrl;
    }

    /// <summary>
    /// Seeds a private UploadFile and returns it with its owner. Callers must act as the owner
    /// (or an audience the file is shared with) — every action now requires
    /// <c>FileAudienceAccess.CanViewFileAsync</c>.
    /// </summary>
    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(IDbContextFactory<BenDataContext> factory)
    {
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "audio.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
            FileSize = 100, FileData = new byte[4],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    private static async Task<Guid> SeedMarkerAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, double timeSeconds = 10,
        string label = "Whisper?", EvpConfidenceLevel confidence = EvpConfidenceLevel.Possible,
        Guid? createdBy = null,
        EvpReviewStatus reviewStatus = EvpReviewStatus.Confirmed,
        double? endSeconds = null,
        bool isAutoDetected = false,
        float? detectionScore = null)
    {
        var markerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AudioMarkers.Add(new AudioMarker
        {
            Id = markerId, UploadFileId = fileId,
            TimeSeconds = timeSeconds, EndSeconds = endSeconds,
            Label = label, ConfidenceLevel = confidence,
            IsAutoDetected = isAutoDetected, DetectionScore = detectionScore,
            ReviewStatus = reviewStatus,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = createdBy ?? Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return markerId;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoMarkers()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.GetAll(fileId, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var markers = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value);
        Assert.Empty(markers);
    }

    [Fact]
    public async Task GetAll_ReturnsMarkers_OrderedByTimeSeconds()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 30.0);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 5.0);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 15.0);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.GetAll(fileId, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var markers = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value).ToList();
        Assert.Equal(3, markers.Count);
        Assert.Equal(5.0, markers[0].TimeSeconds);
        Assert.Equal(15.0, markers[1].TimeSeconds);
        Assert.Equal(30.0, markers[2].TimeSeconds);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.GetById(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsRecord_WhenMarkerExists()
    {
        var factory  = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, timeSeconds: 42.0, label: "Name?");
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.GetById(fileId, markerId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AudioMarkerRecord>(ok.Value);
        Assert.Equal(markerId, record.Id);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal("Name?", record.Label);
        Assert.Equal(42.0, record.TimeSeconds);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMarkerExistsOnDifferentFile()
    {
        var factory  = CreateFactory();
        var (fileId1, _)       = await SeedFileAsync(factory);
        var (fileId2, owner2Id) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId1);
        var ctrl     = Build(factory, owner2Id);

        var result   = await ctrl.GetById(fileId2, markerId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsNotFound_WhenFileDoesNotExist()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Create(Guid.NewGuid(),
            new CreateAudioMarkerRequest(5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, userId: null); // no user

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_Returns201_WithCorrectFields()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(12.5, "Footsteps", EvpConfidenceLevel.Confirmed, "Very clear"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<AudioMarkerRecord>(created.Value);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal(12.5, record.TimeSeconds);
        Assert.Equal("Footsteps", record.Label);
        Assert.Equal(EvpConfidenceLevel.Confirmed, record.ConfidenceLevel);
        Assert.Equal("Very clear", record.Note);
        Assert.Equal(ownerId, record.CreatedByAppUserId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Update(fileId, Guid.NewGuid(),
            new UpdateAudioMarkerRequest(5.0, "new", EvpConfidenceLevel.Probable, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ChangesLabelConfidenceAndNote()
    {
        var factory  = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, label: "old", confidence: EvpConfidenceLevel.Possible);
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.Update(fileId, markerId,
            new UpdateAudioMarkerRequest(20.0, "new", EvpConfidenceLevel.Confirmed, "elaborated"), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AudioMarkerRecord>(ok.Value);
        Assert.Equal("new", record.Label);
        Assert.Equal(EvpConfidenceLevel.Confirmed, record.ConfidenceLevel);
        Assert.Equal("elaborated", record.Note);
        Assert.Equal(20.0, record.TimeSeconds);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenMarkerDeleted()
    {
        var factory  = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId);
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.Delete(fileId, markerId, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.AudioMarkers.FindAsync(markerId));
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Delete(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── File-audience access ──────────────────────────────────────────────────
    // These endpoints previously had no check on the parent file at all: any authenticated
    // user could read, add, rewrite or delete EVP markers on anyone else's private recording
    // just by knowing (or guessing) its id. Markers quote timestamps out of the audio, so
    // reading them leaks the content of the file itself.

    [Fact]
    public async Task GetAll_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        await SeedMarkerAsync(factory, fileId);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetAll(fileId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetById_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        var markerId    = await SeedMarkerAsync(factory, fileId);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetById(fileId, markerId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Create_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Update_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        var markerId    = await SeedMarkerAsync(factory, fileId);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.Update(fileId, markerId,
            new UpdateAudioMarkerRequest(5.0, "hijacked", EvpConfidenceLevel.Confirmed, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Delete_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        var markerId    = await SeedMarkerAsync(factory, fileId);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.Delete(fileId, markerId, default);

        Assert.IsType<ForbidResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.NotNull(await db.AudioMarkers.FindAsync(markerId));
    }

    [Fact]
    public async Task GetAll_PublicFile_AllowsAnyAuthenticatedCaller()
    {
        // The guard must not over-reach: a public file stays readable by everyone.
        var factory = CreateFactory();
        var fileId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
                FileName = "public.mp3", StoredFileName = "p.mp3", ContentType = "audio/mpeg",
                FileSize = 100, FileData = new byte[4], IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }
        await SeedMarkerAsync(factory, fileId);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetAll(fileId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value));
    }

    // ── Marker authorship (moderation) ────────────────────────────────────────

    [Fact]
    public async Task Update_ViewerWhoIsNotAuthorOrFileOwner_ReturnsForbid()
    {
        // Seeing a shared file is enough to add your own markers, but not to rewrite
        // someone else's — mirrors UploadFileCommentController's author-or-owner rule.
        var factory = CreateFactory();
        var fileId  = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
                FileName = "shared.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
                FileSize = 100, FileData = new byte[4], IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }
        var markerId = await SeedMarkerAsync(factory, fileId);   // authored by someone else
        var ctrl = Build(factory, viewerId);

        var result = await ctrl.Update(fileId, markerId,
            new UpdateAudioMarkerRequest(5.0, "hijacked", EvpConfidenceLevel.Confirmed, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Delete_MarkerAuthor_Succeeds_EvenWhenNotFileOwner()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
                FileName = "shared.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
                FileSize = 100, FileData = new byte[4], IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }
        var markerId = await SeedMarkerAsync(factory, fileId, createdBy: authorId);
        var ctrl = Build(factory, authorId);

        var result = await ctrl.Delete(fileId, markerId, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_FileOwner_CanModerate_AnotherUsersMarker()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId);   // authored by someone else
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.Delete(fileId, markerId, default);

        Assert.IsType<NoContentResult>(result);
    }

    // ── ReplaceCandidates ─────────────────────────────────────────────────────

    private static BulkCreateAudioCandidatesRequest Scan(params (double Start, double End, float Score)[] c)
        => new([.. c.Select(x => new AudioCandidateRequest(x.Start, x.End, x.Score))]);

    private static async Task<List<AudioMarker>> MarkersAsync(
        IDbContextFactory<BenDataContext> factory, Guid fileId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AudioMarkers.AsNoTracking()
            .Where(m => m.UploadFileId == fileId).OrderBy(m => m.TimeSeconds).ToListAsync();
    }

    [Fact]
    public async Task ReplaceCandidates_CreatesPendingAutoDetectedSpans()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);

        var result = await Build(factory, ownerId)
            .ReplaceCandidates(fileId, Scan((3.0, 3.6, 72f), (10.0, 10.4, 51f)), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var created = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value).ToList();
        Assert.Equal(2, created.Count);
        Assert.All(created, c =>
        {
            Assert.True(c.IsAutoDetected);
            Assert.Equal(EvpReviewStatus.Pending, c.ReviewStatus);
            Assert.True(c.IsSpan);
        });
        Assert.Equal([3.0, 10.0], created.Select(c => c.TimeSeconds));
        Assert.Equal([72f, 51f], created.Select(c => c.DetectionScore));
    }

    [Fact]
    public async Task ReplaceCandidates_ReplacesOnlyThePriorPendingOnes()
    {
        // The point of the whole review workflow: a re-scan must not wipe what a person decided.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var confirmed = await SeedMarkerAsync(factory, fileId, 1.0, "Real EVP", createdBy: ownerId);
        var dismissed = await SeedMarkerAsync(factory, fileId, 2.0, "Nope", createdBy: ownerId,
                                              reviewStatus: EvpReviewStatus.Dismissed);
        await SeedMarkerAsync(factory, fileId, 5.0, "Detected signal", createdBy: ownerId,
                              reviewStatus: EvpReviewStatus.Pending, isAutoDetected: true);

        await Build(factory, ownerId).ReplaceCandidates(fileId, Scan((9.0, 9.5, 60f)), default);

        var markers = await MarkersAsync(factory, fileId);
        Assert.Equal(3, markers.Count);
        Assert.Contains(markers, m => m.Id == confirmed);
        Assert.Contains(markers, m => m.Id == dismissed);
        Assert.DoesNotContain(markers, m => m.TimeSeconds == 5.0);   // the stale candidate is gone
        Assert.Contains(markers, m => m.TimeSeconds == 9.0);
    }

    [Fact]
    public async Task ReplaceCandidates_WithNoCandidates_ClearsThePendingQueue()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        await SeedMarkerAsync(factory, fileId, 5.0, createdBy: ownerId,
                              reviewStatus: EvpReviewStatus.Pending, isAutoDetected: true);

        await Build(factory, ownerId).ReplaceCandidates(fileId, Scan(), default);

        Assert.Empty(await MarkersAsync(factory, fileId));
    }

    [Theory]
    [InlineData(5.0, 5.0, 50f)]     // zero-length
    [InlineData(5.0, 4.0, 50f)]     // inverted
    [InlineData(-1.0, 2.0, 50f)]    // before the recording starts
    [InlineData(1.0, 2.0, 101f)]    // score out of range
    [InlineData(1.0, 2.0, -1f)]
    public async Task ReplaceCandidates_RejectsMalformedCandidates(double start, double end, float score)
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);

        var result = await Build(factory, ownerId)
            .ReplaceCandidates(fileId, Scan((start, end, score)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await MarkersAsync(factory, fileId));
    }

    [Fact]
    public async Task ReplaceCandidates_RejectsAScanOverTheCap()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var tooMany = Enumerable.Range(0, AudioMarkerController.MaxCandidatesPerScan + 1)
            .Select(i => ((double)i, i + 0.5, 50f)).ToArray();

        var result = await Build(factory, ownerId).ReplaceCandidates(fileId, Scan(tooMany), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await MarkersAsync(factory, fileId));
    }

    [Fact]
    public async Task ReplaceCandidates_ReturnsForbid_ForSomeoneWhoCannotSeeTheFile()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);

        var result = await Build(factory, Guid.NewGuid())
            .ReplaceCandidates(fileId, Scan((1.0, 2.0, 50f)), default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(await MarkersAsync(factory, fileId));
    }

    [Fact]
    public async Task ReplaceCandidates_ReturnsNotFound_ForAnUnknownFile()
    {
        var factory = CreateFactory();
        var result = await Build(factory, Guid.NewGuid())
            .ReplaceCandidates(Guid.NewGuid(), Scan((1.0, 2.0, 50f)), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ── Review ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Review_Confirm_KeepsTheDetectorsScoreAlongsideTheReviewersLabel()
    {
        // Confirming edits the candidate in place rather than copying it, so you can still see what
        // the machine thought of something a person signed off on.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, 3.0, "Detected signal", createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending,
                                             endSeconds: 3.6, isAutoDetected: true, detectionScore: 81f);

        var result = await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Confirmed, "Says my name",
                EvpConfidenceLevel.Probable, "Clear on headphones"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AudioMarkerRecord>(ok.Value);
        Assert.Equal(EvpReviewStatus.Confirmed, record.ReviewStatus);
        Assert.Equal("Says my name", record.Label);
        Assert.Equal(EvpConfidenceLevel.Probable, record.ConfidenceLevel);
        Assert.Equal("Clear on headphones", record.Note);
        Assert.True(record.IsAutoDetected);
        Assert.Equal(81f, record.DetectionScore);
    }

    [Fact]
    public async Task Review_Confirm_CanNudgeTheBounds()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, 3.0, createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending, endSeconds: 3.6);

        var result = await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Confirmed, "Adjusted",
                StartSeconds: 2.8, EndSeconds: 3.9), default);

        var record = Assert.IsType<AudioMarkerRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2.8, record.TimeSeconds);
        Assert.Equal(3.9, record.EndSeconds);
    }

    [Fact]
    public async Task Review_Dismiss_KeepsTheRowSoARescanCanDedupeAgainstIt()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, 3.0, createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending, endSeconds: 3.6);

        await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Dismissed), default);

        var marker = Assert.Single(await MarkersAsync(factory, fileId));
        Assert.Equal(EvpReviewStatus.Dismissed, marker.ReviewStatus);
    }

    [Fact]
    public async Task Review_Dismiss_IgnoresLabelAndConfidence()
    {
        // A rejected candidate shouldn't quietly acquire a reviewer's label.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, 3.0, "Detected signal", createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending, endSeconds: 3.6);

        await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Dismissed, "Should not stick",
                EvpConfidenceLevel.Confirmed), default);

        var marker = Assert.Single(await MarkersAsync(factory, fileId));
        Assert.Equal("Detected signal", marker.Label);
        Assert.Equal(EvpConfidenceLevel.Possible, marker.ConfidenceLevel);
    }

    [Fact]
    public async Task Review_RejectsPendingAsADecision()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending);

        var result = await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Pending), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Review_RejectsAnInvertedSpan()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending);

        var result = await Build(factory, ownerId).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Confirmed,
                StartSeconds: 5.0, EndSeconds: 4.0), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Review_ReturnsForbid_ForAMarkerTheCallerCanSeeButDoesNotOwn()
    {
        // Both gates return Forbid, so this first proves the caller passes the *visibility* gate —
        // otherwise the assertion below would hold for the wrong reason and the author-or-owner
        // rule would be untested.
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, reviewStatus: EvpReviewStatus.Pending);
        var otherViewer = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var file = await db.UploadFiles.FirstAsync(f => f.Id == fileId);
            file.IsPublic = true;
            await db.SaveChangesAsync();
        }

        var canRead = await Build(factory, otherViewer).GetAll(fileId, default);
        Assert.IsType<OkObjectResult>(canRead.Result);

        var result = await Build(factory, otherViewer).Review(fileId, markerId,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Dismissed), default);

        Assert.IsType<ForbidResult>(result.Result);
        var marker = Assert.Single(await MarkersAsync(factory, fileId));
        Assert.Equal(EvpReviewStatus.Pending, marker.ReviewStatus);   // and nothing changed
    }

    [Fact]
    public async Task Review_ReturnsNotFound_ForAnUnknownMarker()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);

        var result = await Build(factory, ownerId).Review(fileId, Guid.NewGuid(),
            new ReviewAudioMarkerRequest(EvpReviewStatus.Dismissed), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A real WAV with two clear voice-band utterances, so the scan path is exercised end to end
    /// rather than against a stub detector.
    /// </summary>
    private static byte[] BuildScannableWav(int sampleRate = 16000)
    {
        var mono = new float[20 * sampleRate];
        var rng = new Random(2024);
        for (var i = 0; i < mono.Length; i++) mono[i] = (float)((rng.NextDouble() * 2 - 1) * 0.004);

        void Utterance(double at, double seconds, double amplitude)
        {
            var start = (int)(at * sampleRate);
            var count = (int)(seconds * sampleRate);
            for (var i = 0; i < count && start + i < mono.Length; i++)
            {
                var t = i / (double)sampleRate;
                var env = (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4.0 * t))
                        * Math.Min(1.0, Math.Min(i, count - i) / (0.02 * sampleRate));
                var tone = (Math.Sin(2 * Math.PI * 500 * t) + Math.Sin(2 * Math.PI * 1500 * t)
                          + Math.Sin(2 * Math.PI * 2500 * t)) / 3.0;
                mono[start + i] += (float)(tone * amplitude * env);
            }
        }
        Utterance(5.0, 0.8, 0.030);
        Utterance(12.0, 0.7, 0.030);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var dataBytes = mono.Length * 2;
            w.Write("RIFF"u8);  w.Write(36 + dataBytes);  w.Write("WAVE"u8);
            w.Write("fmt "u8);  w.Write(16);              w.Write((short)1);
            w.Write((short)1);  w.Write(sampleRate);      w.Write(sampleRate * 2);
            w.Write((short)2);  w.Write((short)16);       w.Write("data"u8);
            w.Write(dataBytes);
            foreach (var v in mono) w.Write((short)Math.Clamp(v * 32767f, short.MinValue, short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Seeds a WAV whose bytes live inline, so no storage service is involved.</summary>
    private static async Task<(Guid FileId, Guid OwnerId)> SeedWavFileAsync(
        IDbContextFactory<BenDataContext> factory, string contentType = "audio/wav")
    {
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var bytes   = BuildScannableWav();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "evp.wav", StoredFileName = "evp.wav", ContentType = contentType,
            FileSize = bytes.Length, FileData = bytes,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    [Fact]
    public async Task Scan_FindsTheUtterancesAndStoresThemAsPendingCandidates()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        var result = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var created = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value).ToList();
        Assert.Equal(2, created.Count);
        Assert.All(created, c =>
        {
            Assert.True(c.IsAutoDetected);
            Assert.Equal(EvpReviewStatus.Pending, c.ReviewStatus);
            Assert.NotNull(c.DetectionScore);
            Assert.True(c.IsSpan);
        });
        // Context padding means the span starts before the utterance and ends after it.
        Assert.Contains(created, c => c.TimeSeconds < 5.0 && c.EndSeconds > 5.8);
        Assert.Contains(created, c => c.TimeSeconds < 12.0 && c.EndSeconds > 12.7);
    }

    [Fact]
    public async Task Scan_DoesNotReproposeSomethingAlreadyDismissed()
    {
        // Without this the review queue never converges: every re-scan hands back the same noise
        // the reviewer just rejected.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        var first = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var firstList = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(first.Result).Value).ToList();

        await Build(factory, ownerId).Review(fileId, firstList[0].Id,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Dismissed), default);

        var second = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var secondList = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(second.Result).Value).ToList();

        Assert.Single(secondList);
        Assert.DoesNotContain(secondList, c => Math.Abs(c.TimeSeconds - firstList[0].TimeSeconds) < 0.5);

        // The dismissed row survives the re-scan, which is what makes the dedupe possible.
        var all = await MarkersAsync(factory, fileId);
        Assert.Contains(all, m => m.ReviewStatus == EvpReviewStatus.Dismissed);
    }

    [Fact]
    public async Task Scan_DoesNotReproposeSomethingAlreadyConfirmed()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        var first = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var firstList = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(first.Result).Value).ToList();

        await Build(factory, ownerId).Review(fileId, firstList[0].Id,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Confirmed, "A voice"), default);

        var second = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var secondList = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(second.Result).Value).ToList();

        Assert.Single(secondList);
        var confirmed = (await MarkersAsync(factory, fileId))
            .Single(m => m.ReviewStatus == EvpReviewStatus.Confirmed);
        Assert.Equal("A voice", confirmed.Label);
    }

    [Fact]
    public async Task Scan_SkipsAHandPlacedPointMarkerItWouldCover()
    {
        // A point marker has no span, so overlap is "does the proposal contain it".
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 5.2, label: "Heard it", createdBy: ownerId);

        var result = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var created = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Single(created);
        Assert.True(created[0].TimeSeconds > 10.0, "the 5s utterance should have been skipped");
    }

    [Fact]
    public async Task Scan_ReplacesThePreviousScansCandidates()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);

        // Two scans, not four candidates.
        Assert.Equal(2, (await MarkersAsync(factory, fileId)).Count);
    }

    [Fact]
    public async Task Scan_RejectsANonAudioFile()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory, contentType: "image/png");

        var result = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Scan_ReturnsBadRequest_WhenTheAudioCannotBeDecoded()
    {
        // Garbage bytes with an audio content type: a problem with this file, not a server fault.
        var factory = CreateFactory();
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "broken.wav", StoredFileName = "broken.wav", ContentType = "audio/wav",
                FileSize = 8, FileData = [1, 2, 3, 4, 5, 6, 7, 8],
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Scan_ReturnsForbid_ForSomeoneWhoCannotSeeTheFile()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedWavFileAsync(factory);

        var result = await Build(factory, Guid.NewGuid()).Scan(fileId, EvpSensitivity.Medium, null, default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(await MarkersAsync(factory, fileId));
    }

    [Fact]
    public async Task Scan_ReturnsNotFound_ForAnUnknownFile()
    {
        var result = await Build(CreateFactory(), Guid.NewGuid())
            .Scan(Guid.NewGuid(), EvpSensitivity.Medium, null, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Scan_RejectsAnUndefinedSensitivity()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        var result = await Build(factory, ownerId).Scan(fileId, (EvpSensitivity)99, null, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Scan_ReturnsUnauthorized_WithoutAUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null)
            .Scan(Guid.NewGuid(), EvpSensitivity.Medium, null, default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    // ── Adjusting bounds without deciding ─────────────────────────────────────

    [Fact]
    public async Task Update_OnAPendingCandidate_MovesTheBoundsAndLeavesItPending()
    {
        // Adjust-bounds routes through Update rather than Review on purpose: moving the edges is
        // not a verdict, and Review refuses anything that isn't confirm-or-dismiss. If Update ever
        // started touching ReviewStatus, adjusting a candidate would silently decide it.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, 12.0, "Detected signal", createdBy: ownerId,
                                             reviewStatus: EvpReviewStatus.Pending,
                                             endSeconds: 12.9, isAutoDetected: true, detectionScore: 77f);

        var result = await Build(factory, ownerId).Update(fileId, markerId,
            new UpdateAudioMarkerRequest(11.6, "Detected signal", EvpConfidenceLevel.Possible, null, 13.4), default);

        var record = Assert.IsType<AudioMarkerRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(11.6, record.TimeSeconds);
        Assert.Equal(13.4, record.EndSeconds);
        Assert.Equal(EvpReviewStatus.Pending, record.ReviewStatus);
        Assert.True(record.IsAutoDetected);
        Assert.Equal(77f, record.DetectionScore);
    }

    [Fact]
    public async Task Scan_AfterAdjustingACandidatesBounds_StillReplacesIt()
    {
        // An adjusted-but-undecided candidate is still Pending, so a re-scan is entitled to
        // replace it. Pinned so the adjust flow doesn't accidentally make candidates sticky.
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedWavFileAsync(factory);

        var first = await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);
        var firstList = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(
            Assert.IsType<OkObjectResult>(first.Result).Value).ToList();

        await Build(factory, ownerId).Update(fileId, firstList[0].Id,
            new UpdateAudioMarkerRequest(1.0, firstList[0].Label, firstList[0].ConfidenceLevel, null, 2.0), default);

        await Build(factory, ownerId).Scan(fileId, EvpSensitivity.Medium, null, default);

        var all = await MarkersAsync(factory, fileId);
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, m => m.TimeSeconds == 1.0);   // the adjusted one was replaced
    }

    // ── The span checks Create and Update never made (finding 8) ──────────────

    /// <summary>
    /// Review and Candidates on this same controller already refused an inverted span. Create and
    /// Update did not, so a marker could be stored that ends before it starts — and every reader of
    /// it, the waveform included, has to decide for itself what that means.
    /// </summary>
    [Fact]
    public async Task Create_RejectsASpanThatEndsBeforeItStarts()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(12.0, "Whisper?", EvpConfidenceLevel.Possible, null, EndSeconds: 4.0),
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.AudioMarkers);
    }

    [Fact]
    public async Task Create_RejectsAMarkerBeforeTheRecordingStarts()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(-5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>
    /// The label column holds 200 characters. A longer one threw inside <c>SaveChanges</c>, which
    /// is a 500 for somebody who pasted a sentence into a text box.
    /// </summary>
    [Fact]
    public async Task Create_RejectsALabelLongerThanTheColumn()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(5.0, new string('x', 400), EvpConfidenceLevel.Possible, null),
            default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("200", bad.Value?.ToString());
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.AudioMarkers);
    }

    [Fact]
    public async Task Update_RejectsASpanThatEndsBeforeItStarts()
    {
        var factory  = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, timeSeconds: 5.0, label: "Whisper?");
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.Update(fileId, markerId,
            new UpdateAudioMarkerRequest(30.0, "Whisper?", EvpConfidenceLevel.Possible, null, EndSeconds: 10.0),
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(5.0, (await db.AudioMarkers.FirstAsync(m => m.Id == markerId)).TimeSeconds);
    }
}
