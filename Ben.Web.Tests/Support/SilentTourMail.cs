namespace Ben.Web.Tests.Support;

/// <summary>
/// A tour mailer for suites that are not about tour mail (item 233).
/// </summary>
/// <remarks>
/// Three controllers and a job gained this dependency when a tour's guest mail arrived. Their
/// existing tests are about attendance, slugs and reminders, so they take one that is wired to an
/// unconfigured email service and therefore sends nothing — the same state every one of those
/// suites already ran in.
/// </remarks>
internal static class SilentTourMail
{
    private sealed class Unconfigured : Ben.Data.Common.Interfaces.IEmailService
    {
        public bool IsConfigured => false;
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    public static Ben.Data.WebApi.Services.Tours.TourGuestMailer Instance { get; } =
        new(new Unconfigured(),
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.Tours.TourGuestMailer>.Instance);
}
