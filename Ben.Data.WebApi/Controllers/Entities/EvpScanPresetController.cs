using System.Text.Json;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Audio;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A person's own named EVP scan settings (phase 5, 10/26/2026).
/// </summary>
/// <remarks>
/// <para>Only ever the caller's: another person's preset is a 404, never a 403, so its existence
/// is not given away. At most one is the default, and the database holds that too.</para>
/// <para>Every stored set of settings is checked again on the way out. A preset saved before a
/// range was tightened comes back with <see cref="EvpScanPresetRecord.Problem"/> rather than
/// quietly running with values the scan would refuse.</para>
/// </remarks>
[ApiController]
[Route("api/evp-scan-presets")]
[Authorize]
public sealed class EvpScanPresetController(IDbContextFactory<BenDataContext> dbContextFactory) : BenControllerBase
{
    /// <summary>How many one person may keep. Plenty for "Basement", "Attic", "Car park"…</summary>
    public const int MaxPresetsPerPerson = 50;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EvpScanPresetRecord>>> Mine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var rows = await db.EvpScanPresets.AsNoTracking()
            .Where(p => p.AppUserId == userId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        return Ok(rows.Select(ToRecord).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<EvpScanPresetRecord>> Create([FromBody] SaveEvpScanPresetRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (Problem(request) is { } problem) return BadRequest(problem);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var name = request.Name.Trim();
        if (await db.EvpScanPresets.AnyAsync(p => p.AppUserId == userId && p.Name == name, ct))
            return Conflict($"You already have a preset called \"{name}\".");
        if (await db.EvpScanPresets.CountAsync(p => p.AppUserId == userId, ct) >= MaxPresetsPerPerson)
            return BadRequest($"You can keep up to {MaxPresetsPerPerson} presets. Delete one you no longer use first.");

        var preset = new EvpScanPreset
        {
            Id = Guid.NewGuid(), AppUserId = userId, Name = name,
            OptionsJson = JsonSerializer.Serialize(request.Options, EvpScanRecorder.OptionsJson),
            DateCreated = DateTime.UtcNow,
        };
        db.EvpScanPresets.Add(preset);
        await SaveWithDefaultAsync(db, userId, preset, request.IsDefault, ct);

        return Ok(ToRecord(preset));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EvpScanPresetRecord>> Update(Guid id, [FromBody] SaveEvpScanPresetRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (Problem(request) is { } problem) return BadRequest(problem);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var preset = await db.EvpScanPresets.FirstOrDefaultAsync(p => p.Id == id && p.AppUserId == userId, ct);
        if (preset is null) return NotFound("That preset isn't yours, or no longer exists.");

        var name = request.Name.Trim();
        if (await db.EvpScanPresets.AnyAsync(p => p.AppUserId == userId && p.Name == name && p.Id != id, ct))
            return Conflict($"You already have a preset called \"{name}\".");

        preset.Name        = name;
        preset.OptionsJson = JsonSerializer.Serialize(request.Options, EvpScanRecorder.OptionsJson);
        preset.DateUpdated = DateTime.UtcNow;
        await SaveWithDefaultAsync(db, userId, preset, request.IsDefault, ct);

        return Ok(ToRecord(preset));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var preset = await db.EvpScanPresets.FirstOrDefaultAsync(p => p.Id == id && p.AppUserId == userId, ct);
        if (preset is null) return NotFound("That preset isn't yours, or no longer exists.");

        db.EvpScanPresets.Remove(preset);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? Problem(SaveEvpScanPresetRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) return "A preset needs a name of 1 to 60 characters.";
        if (request.Options is null) return "A preset needs its settings.";
        return request.Options.Validate();
    }

    /// <summary>
    /// Saves <paramref name="preset"/>, and when it is to be the default, takes that from the old
    /// one first. Two saves in one transaction: the filtered unique index would refuse a single
    /// save that briefly held two defaults, whichever order the database met them in.
    /// </summary>
    private static async Task SaveWithDefaultAsync(
        BenDataContext db, Guid userId, EvpScanPreset preset, bool makeDefault, CancellationToken ct)
    {
        var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await using (transaction)
        {
            if (makeDefault)
            {
                var others = await db.EvpScanPresets
                    .Where(p => p.AppUserId == userId && p.IsDefault && p.Id != preset.Id).ToListAsync(ct);
                foreach (var other in others) other.IsDefault = false;
                await db.SaveChangesAsync(ct);
            }
            preset.IsDefault = makeDefault;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
    }

    private static EvpScanPresetRecord ToRecord(EvpScanPreset preset)
    {
        EvpDetectionOptions? options = null;
        try { options = JsonSerializer.Deserialize<EvpDetectionOptions>(preset.OptionsJson, EvpScanRecorder.OptionsJson); }
        catch (JsonException) { }

        return options is null
            ? new EvpScanPresetRecord(preset.Id, preset.Name, new EvpDetectionOptions(), preset.IsDefault,
                "These settings could not be read. Save the preset again to replace them.")
            : new EvpScanPresetRecord(preset.Id, preset.Name, options, preset.IsDefault, options.Validate());
    }
}
