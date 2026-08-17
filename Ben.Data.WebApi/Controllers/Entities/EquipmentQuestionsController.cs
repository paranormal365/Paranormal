using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// An item's FAQ, written by whoever looks after it, and the anonymous question channel that feeds
/// it.
/// </summary>
/// <remarks>
/// <para>Two halves of one idea, in one controller because they share a visibility rule and one
/// promotes into the other. The <b>FAQ</b> is public-facing and unattributed. A <b>question</b> is a
/// private thread whose two ends never learn each other's names.</para>
///
/// <para>The anonymity is enforced by <i>shape</i>, not by remembering to null a field:
/// <see cref="ReceivedQuestionRecord"/> has no asker id and no asker name to fill in, and the notice
/// that announces a question is sent with <c>HideSenderIdentity</c> so the inbox cannot name the
/// sender either. Both halves are needed — phase 6a found the inbox naming every sender, falling
/// back to their email address, which would have defeated this entirely.</para>
/// </remarks>
[ApiController]
[Route("api/equipment")]
public sealed class EquipmentQuestionsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;

    public EquipmentQuestionsController(IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security)
    {
        _db       = db;
        _security = security;
    }

    // ── The FAQ ──────────────────────────────────────────────────────────────

    /// <summary>An item's FAQ, for anyone who may see the item.</summary>
    [HttpGet("items/{id:guid}/faqs")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<EquipmentFaqRecord>>> GetFaqs(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var (item, audience) = await ResolveAsync(db, id, ct);
        if (item is null) return NotFound();

        var faqs = await db.EquipmentItemFaqs.AsNoTracking()
            .Where(f => f.EquipmentItemId == id)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.DateCreated)
            .Select(f => new EquipmentFaqRecord(f.Id, f.Question, f.Answer, f.SortOrder))
            .ToListAsync(ct);

        _ = audience;   // visibility already decided by ResolveAsync; the FAQ adds no rule of its own
        return Ok(faqs);
    }

    [HttpPost("items/{id:guid}/faqs")]
    [Authorize]
    public async Task<ActionResult<EquipmentFaqRecord>> AddFaq(
        Guid id, [FromBody] UpsertEquipmentFaqRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest("A question and an answer are both needed.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var (item, audience) = await ResolveAsync(db, id, ct);
        if (item is null) return NotFound();
        if (!EquipmentAccess.IsCustodian(audience)) return NotFound();

        var faq = new EquipmentItemFaq
        {
            Id                 = Guid.NewGuid(),
            EquipmentItemId    = id,
            Question           = request.Question.Trim(),
            Answer             = request.Answer.Trim(),
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = GetCurrentUserId(),
        };
        db.EquipmentItemFaqs.Add(faq);
        await db.SaveChangesAsync(ct);

        return Ok(new EquipmentFaqRecord(faq.Id, faq.Question, faq.Answer, faq.SortOrder));
    }

    [HttpPut("items/{id:guid}/faqs/{faqId:guid}")]
    [Authorize]
    public async Task<ActionResult<EquipmentFaqRecord>> UpdateFaq(
        Guid id, Guid faqId, [FromBody] UpsertEquipmentFaqRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest("A question and an answer are both needed.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var (item, audience) = await ResolveAsync(db, id, ct);
        if (item is null || !EquipmentAccess.IsCustodian(audience)) return NotFound();

        var faq = await db.EquipmentItemFaqs.FirstOrDefaultAsync(f => f.Id == faqId && f.EquipmentItemId == id, ct);
        if (faq is null) return NotFound();

        faq.Question           = request.Question.Trim();
        faq.Answer             = request.Answer.Trim();
        faq.SortOrder          = request.SortOrder;
        faq.DateUpdated        = DateTime.UtcNow;
        faq.UpdatedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);

        return Ok(new EquipmentFaqRecord(faq.Id, faq.Question, faq.Answer, faq.SortOrder));
    }

    [HttpDelete("items/{id:guid}/faqs/{faqId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteFaq(Guid id, Guid faqId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var (item, audience) = await ResolveAsync(db, id, ct);
        if (item is null || !EquipmentAccess.IsCustodian(audience)) return NotFound();

        var faq = await db.EquipmentItemFaqs.FirstOrDefaultAsync(f => f.Id == faqId && f.EquipmentItemId == id, ct);
        if (faq is null) return NotFound();

        db.EquipmentItemFaqs.Remove(faq);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Asking ───────────────────────────────────────────────────────────────

    /// <summary>Asks the people who look after a piece of equipment a question about it.</summary>
    /// <remarks>
    /// Refused on your own item (there is nobody to ask) and on retired gear (it is out of
    /// circulation). Both answer 409 rather than 404 — the caller can see the item, so pretending it
    /// does not exist would only be confusing.
    /// </remarks>
    [HttpPost("items/{id:guid}/questions")]
    [Authorize]
    public async Task<ActionResult<AskedQuestionRecord>> AskQuestion(
        Guid id, [FromBody] AskEquipmentQuestionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionText))
            return BadRequest("A question is needed.");

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var (item, audience) = await ResolveAsync(db, id, ct);
        if (item is null) return NotFound();
        if (EquipmentAccess.IsCustodian(audience)) return Conflict("You look after this piece yourself.");
        if (item.IsRetired) return Conflict("That piece has been retired.");

        var question = new EquipmentQuestion
        {
            Id                 = Guid.NewGuid(),
            EquipmentItemId    = id,
            AskedByAppUserId   = userId,
            QuestionText       = request.QuestionText.Trim(),
            Status             = EquipmentQuestionStatus.Open,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.EquipmentQuestions.Add(question);

        await NotifyAnswerersAsync(db, item, userId, ct);
        await db.SaveChangesAsync(ct);

        var model = await ModelNamesAsync(db, item.EquipmentModelId, ct);
        return Ok(new AskedQuestionRecord(
            question.Id, id, item.DisplayName, model.Brand, model.Model,
            question.QuestionText, null, question.Status, question.DateCreated, null));
    }

    // ── My questions, both ways round ────────────────────────────────────────

    /// <summary>Questions this caller has asked. Their own words, and the answer if one came.</summary>
    [HttpGet("/api/me/equipment-questions/asked")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<AskedQuestionRecord>>> GetAsked(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.EquipmentQuestions.AsNoTracking()
            .Where(q => q.AskedByAppUserId == userId)
            .OrderByDescending(q => q.DateCreated)
            .Select(q => new AskedQuestionRecord(
                q.Id,
                q.EquipmentItemId,
                q.EquipmentItem.DisplayName,
                q.EquipmentItem.EquipmentModel.EquipmentBrand.Name,
                q.EquipmentItem.EquipmentModel.Name,
                q.QuestionText,
                q.AnswerText,
                q.Status,
                q.DateCreated,
                q.AnsweredDate))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Questions waiting on this caller, about gear they look after.
    /// </summary>
    /// <remarks>
    /// Built as <see cref="ReceivedQuestionRecord"/>, which has nowhere to put an asker — so the
    /// anonymity survives anyone later editing this query without reading the comment above it.
    /// </remarks>
    [HttpGet("/api/me/equipment-questions/received")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ReceivedQuestionRecord>>> GetReceived(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var itemIds = await AnswerableItemIdsAsync(db, userId, ct);
        if (itemIds.Count == 0) return Ok(Array.Empty<ReceivedQuestionRecord>());

        var rows = await db.EquipmentQuestions.AsNoTracking()
            .Where(q => itemIds.Contains(q.EquipmentItemId))
            .OrderBy(q => q.Status).ThenByDescending(q => q.DateCreated)
            .Select(q => new ReceivedQuestionRecord(
                q.Id,
                q.EquipmentItemId,
                q.EquipmentItem.DisplayName,
                q.EquipmentItem.EquipmentModel.EquipmentBrand.Name,
                q.EquipmentItem.EquipmentModel.Name,
                q.QuestionText,
                q.AnswerText,
                q.Status,
                q.DateCreated,
                q.AnsweredDate,
                q.PromotedToFaqId != null))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Answers an open question, or declines it. Either way the asker is told, anonymously.</summary>
    [HttpPut("/api/me/equipment-questions/{questionId:guid}/answer")]
    [Authorize]
    public async Task<ActionResult<ReceivedQuestionRecord>> AnswerQuestion(
        Guid questionId, [FromBody] AnswerEquipmentQuestionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!request.Decline && string.IsNullOrWhiteSpace(request.AnswerText))
            return BadRequest("An answer is needed, or decline the question.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var question = await db.EquipmentQuestions
            .Include(q => q.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null) return NotFound();

        var audience = await EquipmentAccess.ResolveItemAudienceAsync(
            db, _security, question.EquipmentItem, userId, IsSuperAdmin(), ct);
        if (!EquipmentAccess.IsCustodian(audience)) return NotFound();

        // Answering twice would send the asker a second, different answer to the same question.
        if (question.Status != EquipmentQuestionStatus.Open)
            return Conflict("That question has already been dealt with.");

        question.Status              = request.Decline ? EquipmentQuestionStatus.Declined : EquipmentQuestionStatus.Answered;
        question.AnswerText          = request.Decline ? null : request.AnswerText!.Trim();
        question.AnsweredByAppUserId = userId;
        question.AnsweredDate        = DateTime.UtcNow;
        question.DateUpdated         = DateTime.UtcNow;
        question.UpdatedByAppUserId  = userId;

        NotifyAsker(db, question, userId);
        await db.SaveChangesAsync(ct);

        return Ok(new ReceivedQuestionRecord(
            question.Id, question.EquipmentItemId, question.EquipmentItem.DisplayName,
            question.EquipmentItem.EquipmentModel.EquipmentBrand.Name,
            question.EquipmentItem.EquipmentModel.Name,
            question.QuestionText, question.AnswerText, question.Status,
            question.DateCreated, question.AnsweredDate, question.PromotedToFaqId is not null));
    }

    /// <summary>
    /// Publishes an answered question as an FAQ entry.
    /// </summary>
    /// <remarks>
    /// <b>Copies</b> the text into a new FAQ row rather than publishing the thread. The private
    /// conversation and the public answer are then separate records: editing the FAQ later cannot
    /// rewrite what one person was actually told, and the asker's words can be rephrased for a
    /// general reader without altering the thread. The stamp on the question stops it twice.
    /// </remarks>
    [HttpPost("/api/me/equipment-questions/{questionId:guid}/promote-to-faq")]
    [Authorize]
    public async Task<ActionResult<EquipmentFaqRecord>> PromoteToFaq(
        Guid questionId, [FromBody] PromoteQuestionToFaqRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest("A question and an answer are both needed.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var question = await db.EquipmentQuestions
            .Include(q => q.EquipmentItem)
            .FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null) return NotFound();

        var audience = await EquipmentAccess.ResolveItemAudienceAsync(
            db, _security, question.EquipmentItem, userId, IsSuperAdmin(), ct);
        if (!EquipmentAccess.IsCustodian(audience)) return NotFound();

        if (question.Status != EquipmentQuestionStatus.Answered)
            return Conflict("Only an answered question can be published.");
        if (question.PromotedToFaqId is not null)
            return Conflict("That answer has already been published.");

        var nextSort = await db.EquipmentItemFaqs
            .Where(f => f.EquipmentItemId == question.EquipmentItemId)
            .Select(f => (int?)f.SortOrder).MaxAsync(ct) ?? 0;

        var faq = new EquipmentItemFaq
        {
            Id                 = Guid.NewGuid(),
            EquipmentItemId    = question.EquipmentItemId,
            Question           = request.Question.Trim(),
            Answer             = request.Answer.Trim(),
            SortOrder          = nextSort + 1,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.EquipmentItemFaqs.Add(faq);

        // Same save: a stamp pointing at an FAQ row that failed to commit would block republishing
        // for good.
        question.PromotedToFaqId    = faq.Id;
        question.DateUpdated        = DateTime.UtcNow;
        question.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return Ok(new EquipmentFaqRecord(faq.Id, faq.Question, faq.Answer, faq.SortOrder));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private async Task<(EquipmentItem? Item, EquipmentAccess.ItemAudience Audience)> ResolveAsync(
        BenDataContext db, Guid itemId, CancellationToken ct)
    {
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return (null, EquipmentAccess.ItemAudience.None);

        var audience = await EquipmentAccess.ResolveItemAudienceAsync(
            db, _security, item, GetCurrentUserId(), IsSuperAdmin(), ct);

        return audience == EquipmentAccess.ItemAudience.None ? (null, audience) : (item, audience);
    }

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    /// <summary>Items whose questions this caller may answer: their own, and their groups' gear.</summary>
    private async Task<List<Guid>> AnswerableItemIdsAsync(BenDataContext db, Guid userId, CancellationToken ct)
    {
        var ownItemIds = await db.EquipmentItems.AsNoTracking()
            .Where(i => i.OwnerAppUserId == userId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        foreach (var orgId in orgIds)
        {
            if (!await EquipmentAccess.CanManageOrgEquipmentAsync(
                    _security, userId, orgId, false, OrganizationSecurityAction.Update, ct))
                continue;

            ownItemIds.AddRange(await db.EquipmentItems.AsNoTracking()
                .Where(i => i.OwningOrganizationId == orgId)
                .Select(i => i.Id)
                .ToListAsync(ct));
        }

        return [.. ownItemIds.Distinct()];
    }

    /// <summary>
    /// Tells whoever can answer that a question is waiting — without saying who is asking.
    /// </summary>
    /// <remarks>
    /// The body carries no name and no wording that could narrow it down, and the notice itself is
    /// flagged <c>HideSenderIdentity</c> so the inbox will not name the sender either. Both are
    /// required: the body is what a person reads, the flag is what the projection obeys.
    /// </remarks>
    private async Task NotifyAnswerersAsync(BenDataContext db, EquipmentItem item, Guid askerId, CancellationToken ct)
    {
        var recipients = new List<Guid>();

        if (item.OwnerAppUserId is Guid ownerId)
        {
            recipients.Add(ownerId);
        }
        else if (item.OwningOrganizationId is Guid orgId)
        {
            var memberIds = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .Select(m => m.AppUserId)
                .ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                if (await _security.HasAccessAsync(memberId, orgId,
                        OrganizationSecurityTable.Equipment, OrganizationSecurityAction.Update, ct))
                    recipients.Add(memberId);
            }
        }

        recipients = [.. recipients.Distinct().Where(r => r != askerId)];
        if (recipients.Count == 0) return;

        AddAnonymousMessage(db, recipients, askerId,
            "A question about your equipment",
            $"Someone has asked a question about {NotificationText.Safe(item.DisplayName)}. "
            + "Open My Equipment Questions to answer it. Questions are anonymous in both directions.");
    }

    private static void NotifyAsker(BenDataContext db, EquipmentQuestion question, Guid answererId)
    {
        if (question.AskedByAppUserId == answererId) return;

        var body = question.Status == EquipmentQuestionStatus.Answered
            ? $"Your question about {NotificationText.Safe(question.EquipmentItem.DisplayName)} has been answered."
            : $"Your question about {NotificationText.Safe(question.EquipmentItem.DisplayName)} was closed without an answer.";

        AddAnonymousMessage(db, [question.AskedByAppUserId], answererId, "About your equipment question", body);
    }

    private static void AddAnonymousMessage(
        BenDataContext db, IReadOnlyList<Guid> recipientIds, Guid fromUserId, string subject, string body)
    {
        var message = new UserMessage
        {
            Id                  = Guid.NewGuid(),
            UserMessageTypeId   = OrganizationSeeder.EquipmentQuestionMessageTypeId,
            MessageSubject      = subject,
            MessageBody         = body,
            // The true sender is still stored — abuse has to be traceable — and withheld only from
            // the projection the recipient reads.
            HideSenderIdentity  = true,
            DateCreated         = DateTime.UtcNow,
            CreatedByAppUserId  = fromUserId,
        };
        db.UserMessages.Add(message);

        foreach (var recipientId in recipientIds)
        {
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id          = Guid.NewGuid(),
                MessageId   = message.Id,
                ToAppUserId = recipientId,
            });
        }
    }

    private static async Task<(string Brand, string Model)> ModelNamesAsync(
        BenDataContext db, Guid modelId, CancellationToken ct)
    {
        var names = await db.EquipmentModels.AsNoTracking()
            .Where(m => m.Id == modelId)
            .Select(m => new { Brand = m.EquipmentBrand.Name, Model = m.Name })
            .FirstOrDefaultAsync(ct);
        return (names?.Brand ?? string.Empty, names?.Model ?? string.Empty);
    }
}
