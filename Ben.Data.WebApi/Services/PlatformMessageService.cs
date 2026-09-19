using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Writes a platform message — the kind that lands in the bell and the Messages page.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>AdminAuditLogController.SendMessage</c>, which was the only writer and
/// held the whole mechanism inline. The tier-change notices (item 85's contract arc) need to send
/// the same messages from a controller and from a scheduled job, and three private copies of
/// find-or-create-the-type is how the type ends up duplicated the first time two of them race.</para>
///
/// <para>Recipients that do not exist are skipped rather than failing the send — a message to
/// nine real people and one deleted account should reach nine people.</para>
/// </remarks>
public sealed class PlatformMessageService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public PlatformMessageService(IDbContextFactory<BenDataContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>Sends one message to a set of people. Returns how many actually received it.</summary>
    public async Task<int> SendAsync(
        string subject, string body, IReadOnlyCollection<Guid> recipientUserIds,
        Guid senderUserId, CancellationToken ct)
    {
        if (recipientUserIds.Count == 0) return 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var msgType = await GetOrCreateSystemTypeAsync(db, senderUserId, ct);

        var message = new UserMessage
        {
            Id                 = Guid.NewGuid(),
            UserMessageTypeId  = msgType.Id,
            MessageSubject     = subject,
            MessageBody        = body,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = senderUserId,
        };
        db.UserMessages.Add(message);

        var requested = recipientUserIds.Distinct().ToList();
        var valid = await db.AppUsers.AsNoTracking()
            .Where(u => requested.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var recipientId in valid)
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id            = Guid.NewGuid(),
                MessageId     = message.Id,
                ToAppUserId   = recipientId,
                LastReadCount = 0,
            });

        await db.SaveChangesAsync(ct);
        return valid.Count;
    }

    /// <summary>
    /// The people who should hear about a group's billing: the group's creator plus its nominated
    /// billing contacts, deduplicated.
    /// </summary>
    /// <remarks>
    /// The creator is always included, per item 85's own rule — billing contacts are nominated,
    /// and an empty nomination list is valid, but somebody must always be reachable.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> BillingRecipientsAsync(Guid organizationId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var creator = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => (Guid?)o.CreatedByAppUserId)
            .FirstOrDefaultAsync(ct);

        var contacts = await db.OrganizationBillingContacts.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => c.AppUserId)
            .ToListAsync(ct);

        return [.. contacts.Concat(creator is { } c ? [c] : Array.Empty<Guid>()).Distinct()];
    }

    private static async Task<UserMessageType> GetOrCreateSystemTypeAsync(
        BenDataContext db, Guid senderId, CancellationToken ct)
    {
        var msgType = await db.UserMessageTypes
            .FirstOrDefaultAsync(t => t.Name == "System Notification" && t.IsActive, ct);

        if (msgType is not null) return msgType;

        msgType = new UserMessageType
        {
            Id                 = Guid.NewGuid(),
            Name               = "System Notification",
            Description        = "Automatically generated system messages",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 999,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = senderId,
        };
        db.UserMessageTypes.Add(msgType);
        try
        {
            await db.SaveChangesAsync(ct);
            return msgType;
        }
        catch (DbUpdateException)
        {
            // Lost the race — another request just created the same type. Use theirs.
            db.Entry(msgType).State = EntityState.Detached;
            return await db.UserMessageTypes
                .FirstAsync(t => t.Name == "System Notification" && t.IsActive, ct);
        }
    }
}
