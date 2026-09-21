using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Which letters somebody wants.
/// </summary>
/// <remarks>
/// <para>Item 246 gave nine notices a letter as well as a bell — a cancelled session, a failing
/// payment, a request that went elsewhere — because those are the ones somebody needs while they
/// are not on the site. That is the right default and it is not a licence: a person who would
/// rather hear about it when they next visit should be able to say so.</para>
///
/// <para><b>Only letters that may be declined appear here.</b> Proving an address, resetting a
/// password and a receipt are not choices, and a screen offering to switch off the letter you
/// need to get back into your account would be a defect wearing a feature's clothes. The rule is
/// enforced on the send path as well, so it does not depend on this screen's good manners.</para>
/// </remarks>
[Route("api/me/email-preferences")]
[Authorize]
public sealed class MyEmailPreferencesController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyEmailPreferencesController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Every letter somebody may turn off, and whether they have.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmailPreferenceRecord>>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var declined = await db.UserEmailOptOuts.AsNoTracking()
            .Where(o => o.AppUserId == userId.Value)
            .Select(o => o.Kind)
            .ToListAsync(ct);

        var rows = MailKinds.All
            .Where(k => k.CanDecline)
            .OrderBy(k => k.Title, StringComparer.OrdinalIgnoreCase)
            .Select(k => new EmailPreferenceRecord
            {
                Kind = k.Key,
                Title = k.Title,
                Description = k.Description,
                Wanted = !declined.Contains(k.Key),
            })
            .ToList();

        return Ok(rows);
    }

    /// <summary>Says whether one letter is wanted.</summary>
    /// <remarks>
    /// Idempotent in both directions: asking twice for the same answer is the same answer, which
    /// is what a double click, a retry and two open tabs all amount to.
    /// </remarks>
    [HttpPut("{kind}")]
    public async Task<ActionResult<EmailPreferenceRecord>> Set(
        string kind, [FromBody] SetEmailPreferenceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        if (MailKinds.Find(kind) is not { } info)
            return NotFound("There is no letter by that name.");

        // Said as a sentence rather than a status, because the page shows it to a person.
        if (!info.CanDecline)
            return BadRequest("That letter cannot be switched off — it is how you get back into your account, or a record of something you paid.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var existing = await db.UserEmailOptOuts
            .FirstOrDefaultAsync(o => o.AppUserId == userId.Value && o.Kind == info.Key, ct);

        if (request.Wanted && existing is not null) db.UserEmailOptOuts.Remove(existing);
        else if (!request.Wanted && existing is null)
        {
            db.UserEmailOptOuts.Add(new UserEmailOptOut
            {
                Id = Guid.NewGuid(),
                AppUserId = userId.Value,
                Kind = info.Key,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId.Value,
            });
        }

        await db.SaveChangesAsync(ct);

        return Ok(new EmailPreferenceRecord
        {
            Kind = info.Key,
            Title = info.Title,
            Description = info.Description,
            Wanted = request.Wanted,
        });
    }
}

/// <summary>Whether one letter is wanted.</summary>
public sealed record SetEmailPreferenceRequest(bool Wanted);
