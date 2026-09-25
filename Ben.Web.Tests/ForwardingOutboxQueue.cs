using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;

namespace Ben.Web.Tests;

/// <summary>
/// A queue that hands each letter to an <see cref="IEmailService"/> instead of the caller's context.
/// </summary>
/// <remarks>
/// <para><b>For the tests that are about what a letter SAYS.</b> Most of the hosted-event tests
/// record letters through a fake <see cref="IEmailService"/> and assert on their words. Item 239b
/// moved the decision letter onto <see cref="IOutboxEmailQueue"/> so it commits with the decision,
/// and those tests would have gone quiet — a queued letter never reaches their fake. This puts it
/// there, so they keep checking the words.</para>
///
/// <para><b>It proves nothing about atomicity</b>, and must not be used where that is the point:
/// nothing lands in the caller's context, so nothing rolls back with it. The tests that ARE about
/// that use the real outbox — <c>ABookingDecisionCommitsWithItsLetterTests</c>,
/// <c>ABookingRequestCommitsWithItsLetterTests</c>.</para>
/// </remarks>
internal sealed class ForwardingOutboxQueue(IEmailService email) : IOutboxEmailQueue
{
    public Task EnqueueAsync(BenDataContext callersDb, EmailMessage message, CancellationToken ct = default)
        => email.SendAsync(message, ct);
}
