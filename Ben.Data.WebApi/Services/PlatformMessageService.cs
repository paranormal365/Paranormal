using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
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
    private readonly IEmailService? _email;
    private readonly ILogger<PlatformMessageService>? _log;

    /// <param name="email">
    /// How a notice is also posted, for the callers that ask for it.
    /// <para><b>Optional on purpose.</b> Twenty fixtures build this service to check that a
    /// message was written, and none of them is about mail. The backlog named this exact trap —
    /// "every mailer converted breaks the tests that mock it", twice already — so the dependency
    /// is additive: without it the service does what it always did, and a caller that asks to
    /// post a letter in a fixture that supplied no mailer simply does not.</para>
    /// </param>
    public PlatformMessageService(
        IDbContextFactory<BenDataContext> dbFactory,
        IEmailService? email = null,
        ILogger<PlatformMessageService>? log = null)
    {
        _dbFactory = dbFactory;
        _email = email;
        _log = log;
    }

    /// <summary>Sends one message to a set of people. Returns how many actually received it.</summary>
    /// <param name="alsoPost">
    /// Post this as a letter too, as the named kind.
    /// <para>Optional and trailing, so every existing caller is unchanged. A notice that only ever
    /// lands in the bell reaches somebody who comes back to the site; the ones that take this are
    /// the ones a person needs while they are NOT here — a session cancelled, a payment that did
    /// not go through, an account somebody else made for them (item 246).</para>
    /// <para>Naming the kind is also what lets the letter carry a written template: the composing
    /// happens in the outbox, keyed on the kind, so nothing here has to know templates exist.</para>
    /// </param>
    /// <param name="tablesFor">
    /// The rows each recipient's letter is carrying, when the caller has them — a template reads
    /// <c>{Organizations.Name}</c> from here. Called once per recipient, so a letter can say
    /// something about the person receiving it.
    /// </param>
    public async Task<int> SendAsync(
        string subject, string body, IReadOnlyCollection<Guid> recipientUserIds,
        Guid senderUserId, CancellationToken ct,
        MailKindInfo? alsoPost = null,
        Func<Guid, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>>? tablesFor = null)
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

        if (alsoPost is not null && _email is not null)
            await PostAsync(db, alsoPost, subject, body, valid, tablesFor, ct);

        return valid.Count;
    }

    /// <summary>
    /// Posts the same notice as a letter, to everybody who has an address.
    /// </summary>
    /// <remarks>
    /// <para><b>Never fatal, and never before the message is saved.</b> The in-app notice is the
    /// record; the letter is the copy that reaches somebody who is not here. A mail failure must
    /// not undo a notification that has already landed, so this runs after the save and swallows
    /// what it cannot do — with a line saying so, because a letter nobody can see failing is the
    /// exact silence the outbox exists to end.</para>
    ///
    /// <para>The body arrives as the plain text a platform message carries, so it is wrapped in
    /// the paragraphs a letter needs rather than posted as one unbroken line.</para>
    /// </remarks>
    private async Task PostAsync(
        BenDataContext db, MailKindInfo kind, string subject, string body,
        IReadOnlyCollection<Guid> recipients,
        Func<Guid, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>>? tablesFor,
        CancellationToken ct)
    {
        var addresses = await db.AppUsers.AsNoTracking()
            .Where(u => recipients.Contains(u.Id) && u.Email != null && u.Email != "")
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(ct);

        // A platform message's body is sometimes already markup and sometimes plain text with
        // newlines in it. Wrapping the first would show somebody their own <p> tags; leaving the
        // second would post one unbroken line.
        var html = Ben.Data.Common.Text.PlainTextHtml.LooksLikeHtml(body)
            ? body
            : Ben.Data.Common.Text.PlainTextHtml.FromPlainText(body);

        foreach (var person in addresses)
        {
            try
            {
                var tables = tablesFor?.Invoke(person.Id)
                    ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

                // The person is always available to a template, whatever else the caller supplied.
                if (!tables.ContainsKey("AppUsers"))
                {
                    tables = new Dictionary<string, IReadOnlyDictionary<string, object?>>(
                        tables, StringComparer.OrdinalIgnoreCase)
                    {
                        ["AppUsers"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Email"] = person.Email,
                            ["DisplayName"] = person.DisplayName,
                        },
                    };
                }

                await _email!.SendAsync(
                    new EmailMessage(person.Email!, subject, html,
                                     Kind: kind.Key, Payload: new MailPayload(tables)), ct);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex,
                    "Could not post the {Kind} letter to {Recipient}; the notice reached them on "
                  + "the site but not by mail.", kind.Key, person.Id);
            }
        }
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
