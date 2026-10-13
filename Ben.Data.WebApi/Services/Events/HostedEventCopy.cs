using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The next event, started from the last one (item 235 phase 12).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"Since we know they have an account, we might ask if they want to save the
/// ads, menu whatever they have created for the event — so they can use it again."</i></para>
///
/// <para><b>What comes across is what the organizer made; what does not is what happened.</b> The plan,
/// its blocks, the menus, the programme, the bands, the helpers and the advert are the organizer's own
/// work and are offered. Bookings, passes, arrivals, the room, the photos and the gallery are the last
/// weekend's, and a copy that carried a single one of them would have somebody's name on an evening they
/// never booked.</para>
///
/// <para><b>Everything moves by whole days, on the venue's clock.</b> A Friday-to-Sunday weekend copied
/// to a Friday stays Friday to Sunday; a 7 PM séance stays at 7 PM even when the clocks change between
/// the two dates, which adding hours in UTC would get wrong twice a year.</para>
///
/// <para><b>The agreement with the venue does not come across.</b> A yes for October is not a yes for
/// March: an arrangement recorded by hand keeps the contact's name and loses the date and reference, and
/// one resting on another group's grant has to ask again. The readiness list says so.</para>
///
/// <para>Always a Draft, so copying costs nothing and publishes nothing. Nothing here saves; the caller
/// owns the transaction.</para>
/// </remarks>
public static class HostedEventCopy
{
    /// <summary>Whole days between the source's first date and the copy's.</summary>
    public static int DaysBetween(HostedEvent source, DateTime newStartsOn)
        => (int)(newStartsOn.Date - source.StartsOn.Date).TotalDays;

    /// <summary>A moment moved by whole days as the venue's clock reads it.</summary>
    public static DateTime ShiftUtc(DateTime utc, int days, string? timeZoneId)
    {
        var zone = HostedEventCalendarSync.ZoneOf(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var moved = DateTime.SpecifyKind(local.AddDays(days), DateTimeKind.Unspecified);

        // The hour a clock skips does not exist; a session in it is nudged forward rather than refused.
        if (zone.IsInvalidTime(moved)) moved = moved.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(moved, zone);
    }

    /// <summary>What the copy brought, for the page to say.</summary>
    public sealed record Brought(
        int Nights, int Units, int Blocks, int Menus, int Sessions, int Bands, int Helpers, int Adverts);

    /// <summary>
    /// Writes the copy into the context: the event, its dates and whatever the request asked for.
    /// </summary>
    /// <param name="source">Loaded with its nights.</param>
    public static async Task<(HostedEvent Copy, Brought Brought)> CopyAsync(
        BenDataContext db, HostedEvent source, CopyHostedEventRequest request, Guid actorId, DateTime now,
        CancellationToken ct)
    {
        var days = DaysBetween(source, request.StartsOn);
        var name = request.Name.Trim();

        var copy = new HostedEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = source.OrganizationId,
            Name = name,
            UrlName = await UrlSlug.MakeUniqueAsync(
                UrlSlug.From(name) ?? "event",
                slug => db.HostedEvents.AnyAsync(e => e.OrganizationId == source.OrganizationId && e.UrlName == slug, ct)),
            Tagline = source.Tagline,
            Description = source.Description,
            PlaceId = source.PlaceId,
            HideExactLocation = source.HideExactLocation,
            TimeZoneId = source.TimeZoneId,
            StartsOn = source.StartsOn.Date.AddDays(days),
            EndsOn = source.EndsOn.Date.AddDays(days),
            DatesAreSeparate = source.DatesAreSeparate,
            DefaultStartLocal = source.DefaultStartLocal,
            DefaultEndLocal = source.DefaultEndLocal,
            LifecycleState = HostedEventLifecycleState.Draft,
            DayPassCapacity = source.DayPassCapacity,
            DayPassPrice = source.DayPassPrice,
            LayoutKind = source.LayoutKind,
            BookingsCloseAtUtc = source.BookingsCloseAtUtc is { } closes ? ShiftUtc(closes, days, source.TimeZoneId) : null,
            ContactLine = source.ContactLine,
            BookingMode = source.BookingMode,
            HoldMinutes = source.HoldMinutes,
            PhotoPosting = source.PhotoPosting,

            // THE VENUE'S YES DOES NOT COME ACROSS. See the remarks.
            VenueArrangement = source.VenueArrangement,
            VenueContactName = source.VenueContactName,
            VenueAgreedOnUtc = null,
            VenueReference = null,
            VenueGrantId = null,

            MinimumGuests = source.MinimumGuests,
            GoNoGoDeadlineUtc = source.GoNoGoDeadlineUtc is { } deadline ? ShiftUtc(deadline, days, source.TimeZoneId) : null,
            GoNoGoDecision = HostedEventGoNoGo.Undecided,

            CoverUploadFileId = source.CoverUploadFileId,
            MailSubjectTemplate = source.MailSubjectTemplate,
            MailBodyTemplate = source.MailBodyTemplate,
            CollectsEvidence = source.CollectsEvidence,
            DateCreated = now,
            CreatedByAppUserId = actorId,
        };
        db.HostedEvents.Add(copy);

        // ── the dates, by position ────────────────────────────────────────────
        var oldNights = source.Nights.OrderBy(n => n.Date).ThenBy(n => n.SortOrder).ToList();
        var nightMap = new Dictionary<Guid, Guid>();
        foreach (var night in oldNights)
        {
            var id = Guid.NewGuid();
            nightMap[night.Id] = id;
            db.HostedEventNights.Add(new HostedEventNight
            {
                Id = id, HostedEventId = copy.Id, Date = night.Date.Date.AddDays(days), Title = night.Title,
                StartLocal = night.StartLocal, EndLocal = night.EndLocal, Notes = night.Notes, SortOrder = night.SortOrder,
                DateCreated = now, CreatedByAppUserId = actorId,
            });
        }

        int units = 0, blocks = 0, menus = 0, sessions = 0, bands = 0, helpers = 0, adverts = 0;

        // ── the plan, and what the venue kept back ────────────────────────────
        if (request.Plan)
        {
            var oldUnits = await db.HostedEventLayoutUnits.AsNoTracking()
                .Where(u => u.HostedEventId == source.Id).ToListAsync(ct);
            var unitMap = new Dictionary<Guid, Guid>();

            foreach (var unit in oldUnits)
            {
                var id = Guid.NewGuid();
                unitMap[unit.Id] = id;
                db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
                {
                    Id = id, HostedEventId = copy.Id, PlaceRoomId = unit.PlaceRoomId, Label = unit.Label,
                    Section = unit.Section, Capacity = unit.Capacity, Note = unit.Note, Price = unit.Price,
                    LayoutRow = unit.LayoutRow, LayoutColumn = unit.LayoutColumn, SortOrder = unit.SortOrder,
                    DateCreated = now, CreatedByAppUserId = actorId,
                });
                units++;
            }

            var oldBlocks = await db.HostedEventUnitBlocks.AsNoTracking()
                .Where(b => b.HostedEventLayoutUnit.HostedEventId == source.Id).ToListAsync(ct);
            foreach (var block in oldBlocks)
            {
                if (!unitMap.TryGetValue(block.HostedEventLayoutUnitId, out var unitId)) continue;
                Guid? nightId = null;
                if (block.HostedEventNightId is { } oldNight)
                {
                    if (!nightMap.TryGetValue(oldNight, out var mapped)) continue;
                    nightId = mapped;
                }

                db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
                {
                    Id = Guid.NewGuid(), HostedEventLayoutUnitId = unitId, HostedEventNightId = nightId,
                    Kind = block.Kind, Note = block.Note, DateCreated = now, CreatedByAppUserId = actorId,
                });
                blocks++;
            }
        }
        else
        {
            copy.LayoutKind = source.LayoutKind;
        }

        // ── the meals onto the same night by position, and the designed menus ──
        // Meals carry their time and note; the dishes now live on designed menus (10/13/2026), which
        // come across as drafts served at the copied meals. A meal's old typed dishes come across only
        // while that meal hasn't been converted yet, so the conversion makes the new event's menu from
        // them; once converted, copying them would only make a second menu.
        if (request.Menus)
        {
            var oldMeals = await db.HostedEventMenus.AsNoTracking()
                .Include(m => m.Items)
                .Where(m => m.HostedEventNight.HostedEventId == source.Id).ToListAsync(ct);
            var converted = (await db.EventMenus.AsNoTracking()
                .Where(m => m.HostedEventId == source.Id && m.ConvertedFromHostedEventMenuId != null)
                .Select(m => m.ConvertedFromHostedEventMenuId!.Value).ToListAsync(ct)).ToHashSet();

            var mealMap = new Dictionary<Guid, Guid>();
            foreach (var meal in oldMeals)
            {
                if (!nightMap.TryGetValue(meal.HostedEventNightId, out var nightId)) continue;

                var copied = new HostedEventMenu
                {
                    Id = Guid.NewGuid(), HostedEventNightId = nightId, Title = meal.Title,
                    ServedAtLocal = meal.ServedAtLocal, Notes = meal.Notes, SortOrder = meal.SortOrder,
                    DateCreated = now, CreatedByAppUserId = actorId,
                };
                if (!converted.Contains(meal.Id))
                    foreach (var item in meal.Items)
                        copied.Items.Add(new HostedEventMenuItem
                        {
                            Id = Guid.NewGuid(), HostedEventMenuId = copied.Id, Course = item.Course, Name = item.Name,
                            Description = item.Description, DietaryTags = item.DietaryTags, SortOrder = item.SortOrder,
                            DateCreated = now,
                        });
                mealMap[meal.Id] = copied.Id;
                db.HostedEventMenus.Add(copied);
            }

            var designed = await db.EventMenus.AsNoTracking()
                .Include(m => m.Prices).Include(m => m.Servings)
                .Include(m => m.Sections).ThenInclude(s => s.Entries)
                .Where(m => m.HostedEventId == source.Id).ToListAsync(ct);

            // Items kept for the old event only are copied for the new one; the group's and the
            // venue's are the same items, shared.
            var usedIds = designed.SelectMany(m => m.Sections).SelectMany(s => s.Entries).Select(e => e.MenuLibraryItemId).Distinct().ToList();
            var eventOnly = await db.MenuLibraryItems.AsNoTracking()
                .Where(i => usedIds.Contains(i.Id) && i.Scope == Ben.Data.Common.Enums.MenuItemScope.Event)
                .ToListAsync(ct);
            var itemMap = new Dictionary<Guid, Guid>();
            foreach (var item in eventOnly)
            {
                var copiedItem = new MenuLibraryItem
                {
                    Id = Guid.NewGuid(), OrganizationId = copy.OrganizationId, Scope = item.Scope, HostedEventId = copy.Id,
                    MenuCategoryId = item.MenuCategoryId, Name = item.Name, Description = item.Description,
                    ImageUploadFileId = item.ImageUploadFileId, Dietary = item.Dietary, Allergens = item.Allergens,
                    DietaryNote = item.DietaryNote, DateCreated = now, CreatedByAppUserId = actorId,
                };
                itemMap[item.Id] = copiedItem.Id;
                db.MenuLibraryItems.Add(copiedItem);
            }

            foreach (var menu in designed)
            {
                var copiedMenu = EventMenus.Copy(menu, menu.Title, copy.Id, actorId, now, itemMap);
                copiedMenu.CopiedFromEventMenuId = null;
                foreach (var serving in menu.Servings)
                    if (mealMap.TryGetValue(serving.HostedEventMenuId, out var mealId))
                        copiedMenu.Servings.Add(new EventMenuServing { EventMenuId = copiedMenu.Id, HostedEventMenuId = mealId });
                db.EventMenus.Add(copiedMenu);
            }

            menus = oldMeals.Count(m => mealMap.ContainsKey(m.Id));
        }

        // ── the programme, unpublished, with nobody signed up ─────────────────
        if (request.Programme)
        {
            var oldSessions = await db.HostedEventSessions.AsNoTracking()
                .Where(s => s.HostedEventId == source.Id && s.CalledOffUtc == null).ToListAsync(ct);

            foreach (var session in oldSessions)
            {
                db.HostedEventSessions.Add(new HostedEventSession
                {
                    Id = Guid.NewGuid(), HostedEventId = copy.Id, Title = session.Title, Description = session.Description,
                    StartsAtUtc = ShiftUtc(session.StartsAtUtc, days, source.TimeZoneId),
                    EndsAtUtc = ShiftUtc(session.EndsAtUtc, days, source.TimeZoneId),
                    PlaceRoomId = session.PlaceRoomId, LocationText = session.LocationText, LedBy = session.LedBy,
                    Capacity = session.Capacity, RequiresSignUp = session.RequiresSignUp, PlacesTaken = 0,
                    SortOrder = session.SortOrder, DateCreated = now, CreatedByAppUserId = actorId,
                });
                sessions++;
            }
        }

        // ── the bands ─────────────────────────────────────────────────────────
        if (request.Bands)
        {
            foreach (var band in await db.HostedEventBands.AsNoTracking()
                         .Where(b => b.HostedEventId == source.Id).ToListAsync(ct))
            {
                db.HostedEventBands.Add(new HostedEventBand
                {
                    Id = Guid.NewGuid(), HostedEventId = copy.Id, Colour = band.Colour, Meaning = band.Meaning,
                    Hex = band.Hex, Rule = band.Rule, SortOrder = band.SortOrder,
                    DateCreated = now, CreatedByAppUserId = actorId,
                });
                bands++;
            }
        }

        // ── the helpers who said yes; an invitation nobody answered stays behind ─
        if (request.Helpers)
        {
            foreach (var helper in await db.HostedEventStaff.AsNoTracking()
                         .Where(s => s.HostedEventId == source.Id && s.AppUserId != null).ToListAsync(ct))
            {
                db.HostedEventStaff.Add(new HostedEventStaff
                {
                    Id = Guid.NewGuid(), HostedEventId = copy.Id, AppUserId = helper.AppUserId,
                    DisplayName = helper.DisplayName, RoleLabel = helper.RoleLabel,
                    SeesBookings = helper.SeesBookings, Decides = helper.Decides, RunsTheDoor = helper.RunsTheDoor,
                    SeesMenus = helper.SeesMenus, SeesFiles = helper.SeesFiles,
                    DateConfirmed = now, DateCreated = now, CreatedByAppUserId = actorId,
                });
                helpers++;
            }
        }

        // ── the advert, as a draft that goes through review again ─────────────
        if (request.Adverts)
        {
            foreach (var ad in await db.OrganizationAds.AsNoTracking()
                         .Where(a => a.HostedEventId == source.Id).ToListAsync(ct))
            {
                db.OrganizationAds.Add(new OrganizationAd
                {
                    Id = Guid.NewGuid(), OrganizationId = ad.OrganizationId, Headline = ad.Headline, Body = ad.Body,
                    ImageUploadFileId = ad.ImageUploadFileId, TargetKind = ad.TargetKind, HostedEventId = copy.Id,
                    Status = OrganizationAdStatus.Draft, DateCreated = now, CreatedByAppUserId = actorId,
                });
                adverts++;
            }
        }

        return (copy, new Brought(nightMap.Count, units, blocks, menus, sessions, bands, helpers, adverts));
    }
}
