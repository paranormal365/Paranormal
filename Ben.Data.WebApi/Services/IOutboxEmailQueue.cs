using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Queues a letter inside the caller's own transaction, so the letter and the thing it is about
/// either both happen or neither does (item 239b).
/// </summary>
/// <remarks>
/// <para><b>Why this is not on <see cref="IEmailService"/>.</b> That interface lives in
/// <c>Ben.Data.Common</c>, which cannot see <see cref="BenDataContext"/>, and Identity's own mail
/// goes through <c>IEmailSender</c>, which cannot take a context at all. Both stay on
/// <see cref="IEmailService.SendAsync(EmailMessage, CancellationToken)"/> and are right to.</para>
///
/// <para><b>What it fixes.</b> 239a made every letter an outbox row, but
/// <c>OutboxEmailService.SendAsync</c> opens its OWN context and saves separately. A booking that
/// commits and a confirmation that fails to enqueue leaves a guest with a seat and no letter; a
/// letter enqueued before a save that then rolls back tells somebody they have a seat they do not
/// have. Neither fails loudly — which is the same shape as the retention warnings that were dead
/// for twelve days before anybody noticed, because nothing about a letter that simply never
/// arrives makes a noise.</para>
///
/// <para><b>Use it only where there is something to be atomic WITH.</b> A caller that owns a
/// <see cref="BenDataContext"/> and writes a row the letter is about should use this; a caller
/// that only sends a letter should keep using <c>SendAsync</c>, which handles its own failure and
/// logs it. Passing a context that is not the one being saved would be worse than either.</para>
/// </remarks>
public interface IOutboxEmailQueue
{
    /// <summary>
    /// Adds the letter to the caller's context WITHOUT saving. The caller's own
    /// <c>SaveChangesAsync</c> commits the row and the letter together, or neither.
    /// </summary>
    /// <remarks>
    /// <para><b>This throws where <c>SendAsync</c> swallows,</b> and that is the point. SendAsync
    /// logs and returns, because a letter failing must not take down the request that asked for
    /// it. Here the whole contract is "both or neither": if the letter cannot be queued, the
    /// caller's write must not commit either, so the exception has to reach it.</para>
    /// </remarks>
    Task EnqueueAsync(BenDataContext callersDb, EmailMessage message, CancellationToken ct = default);
}
