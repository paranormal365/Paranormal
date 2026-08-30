using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Places;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Putting one of your own field sessions into a place's public archive, and taking it back out.
/// </summary>
/// <remarks>
/// <para><b>What the archive is for.</b> One person's readings at a location are an anecdote.
/// Twelve people's readings at the same location, over two years, are evidence of something
/// persistent — or evidence that the one spike was noise. Nothing in this field can currently
/// tell those apart, because every recording dies on the phone that made it. This endpoint is
/// the door that ends that.</para>
///
/// <para><b>Public places only, and that is the safety hinge.</b> <see cref="PlaceKind"/>
/// defaults to <see cref="PlaceKind.PrivateResidence"/>, so the refusal here is what stops
/// somebody publishing sensor readings, timings and coordinates taken inside a stranger's home.
/// The paid lane exists precisely so private-residence work never has to be public; this is the
/// same rule arriving from the other side.</para>
///
/// <para><b>Publishing is reversible, deliberately.</b> Somebody who realises they published the
/// wrong night, or simply changes their mind, must be able to take it back without asking
/// anybody — so retraction is a plain DELETE, not a support request. What has already been read
/// by others cannot be unread, and the UI says so before the first publish rather than after the
/// retraction.</para>
///
/// <para><b>The document only, for now.</b> Publishing shares the session's readings — the
/// numbers, the markers, the timeline. Photos and audio stay private until the archive has the
/// same moderation the feed has (screening, reporting, blocking); shipping strangers' media on a
/// public page without it would be the feed's problem with none of the feed's answers.</para>
/// </remarks>
[ApiController]
[Route("api/field-sessions/{id:guid}/publish")]
[Authorize]
public sealed class FieldSessionPublishController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly ILogger<FieldSessionPublishController> _log;

    public FieldSessionPublishController(
        IDbContextFactory<BenDataContext> db, ILogger<FieldSessionPublishController> log)
    {
        _db = db;
        _log = log;
    }

    /// <param name="PlaceId">An existing public location, when the person picked one.</param>
    /// <param name="NewPlace">
    /// Details for a location with no record yet. Exactly one of the two is used, and this one is
    /// why the archive can exist at all: until now a place could ONLY be created through the
    /// investigation flow, which needs an investigation, which needs an organization — so the free
    /// individual the archive is FOR had no way to say where they had been.
    /// </param>
    public sealed record PublishRequest(Guid? PlaceId, NewArchivePlace? NewPlace = null);

    /// <summary>A public location somebody is naming for the first time.</summary>
    /// <remarks>
    /// No <c>Kind</c>: everything created here is a <see cref="PlaceKind.PublicLocation"/> by
    /// construction. Letting the caller choose would hand them the one switch this whole feature's
    /// safety rests on.
    /// </remarks>
    public sealed record NewArchivePlace(
        string Name, string? StreetAddress1, string? City, string? State, string? ZipCode,
        decimal? Latitude, decimal? Longitude);

    /// <summary>Publishes this session to a place's archive.</summary>
    [HttpPost]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == id, ct);
        // Whether somebody else's session exists is not a thing to let an outsider probe for —
        // the same answer as absent, as everywhere else in this controller family.
        if (session is null || session.SubmittedByAppUserId != userId) return NotFound();

        var (place, refusal) = await ResolvePlaceAsync(db, request, userId, ct);
        if (refusal is not null) return BadRequest(refusal);
        if (place is null) return NotFound("That place doesn't exist.");

        if (place.Kind != PlaceKind.PublicLocation)
            return BadRequest(
                "Only public locations have an open archive. A session recorded at somebody's "
              + "home stays with you and your group — that is what the private lane is for.");

        session.PlaceId = place.Id;
        // Re-publishing an already-public session keeps its original date: the answer to "when
        // did this become public" must not move because somebody pressed the button twice.
        session.PublishedAtUtc ??= DateTime.UtcNow;
        session.DateUpdated = DateTime.UtcNow;
        session.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Field session {SessionId} published to place {PlaceId} by {UserId}.",
            id, place.Id, userId);

        return NoContent();
    }

    /// <summary>
    /// Finds the place being named, creating it when it is genuinely new.
    /// </summary>
    /// <remarks>
    /// <para><b>Matching before creating is what makes the archive an archive.</b> Two people who
    /// describe the same cave differently — "Bell Witch Cave" and "Bell Witch Cave, Adams TN" —
    /// must land on ONE place, or the readings never accumulate and the whole feature is a pile of
    /// single-visit pages. <see cref="PlaceMatcher"/> already owns that rule (same address AND
    /// within 0.1 miles); this reuses it rather than inventing a second one.</para>
    ///
    /// <para>A match wins even when it is a private residence — the caller then meets the ordinary
    /// refusal above. Creating a duplicate public place at a home's coordinates to get around that
    /// is exactly the hole this ordering closes.</para>
    /// </remarks>
    private static async Task<(Place? Place, string? Refusal)> ResolvePlaceAsync(
        BenDataContext db, PublishRequest request, Guid userId, CancellationToken ct)
    {
        if (request.PlaceId is { } id && id != Guid.Empty)
            return (await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct), null);

        if (request.NewPlace is not { } fresh)
            return (null, "Say where this was recorded.");

        if (string.IsNullOrWhiteSpace(fresh.Name))
            return (null, "A place needs a name people will recognise.");

        // Candidates are narrowed in the database by state, then judged by the shared matcher —
        // the radius test needs coordinates the query cannot compare.
        var nearby = await db.Places
            .Where(p => fresh.State == null || p.State == fresh.State)
            .ToListAsync(ct);

        var existing = nearby.FirstOrDefault(p => PlaceMatcher.IsProbableMatch(
            p, fresh.StreetAddress1, fresh.City, fresh.State, fresh.ZipCode, fresh.Name,
            fresh.Latitude, fresh.Longitude));

        // The archive's own, looser rule — same spot, one name inside the other — applied only to
        // public locations. Without it "Bell Witch Cave" and "Bell Witch Cave, Adams" become two
        // pages that each look like nobody has been there, which is the one failure this feature
        // cannot survive. Observed on the very first three-person test.
        existing ??= nearby.FirstOrDefault(p => p.Kind == PlaceKind.PublicLocation
            && PlaceMatcher.IsProbableArchiveMatch(p, fresh.Name, fresh.Latitude, fresh.Longitude));

        if (existing is not null) return (existing, null);

        var place = new Place
        {
            Id = Guid.NewGuid(),
            Name = fresh.Name.Trim(),
            StreetAddress1 = fresh.StreetAddress1?.Trim(),
            City = fresh.City?.Trim(),
            State = fresh.State?.Trim(),
            ZipCode = fresh.ZipCode?.Trim(),
            Country = "US",
            Latitude = fresh.Latitude,
            Longitude = fresh.Longitude,
            // Never from the caller: see NewArchivePlace.
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        await PlaceGeocoder.GeocodeAsync(place, trustSuppliedCoordinates: true, ct);
        db.Places.Add(place);
        return (place, null);
    }

    /// <summary>Takes it back out of the archive. The place stays; only the publication ends.</summary>
    [HttpDelete]
    public async Task<IActionResult> Retract(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || session.SubmittedByAppUserId != userId) return NotFound();

        // The PLACE is left alone on purpose: where a session happened is a fact about the
        // recording, not a consequence of having shared it, and somebody who retracts today may
        // republish tomorrow without re-answering "where were you".
        session.PublishedAtUtc = null;
        session.DateUpdated = DateTime.UtcNow;
        session.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
