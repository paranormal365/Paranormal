using Ben.Data.Common.Enums;

namespace Ben.Data.WebApi.Services.Push;

/// <summary>One push: what the person reads, and what the app does when they tap it.</summary>
/// <param name="Title">The bold first line.</param>
/// <param name="Body">The line under it.</param>
/// <param name="Data">Plain string pairs the app reads on a tap (e.g. <c>link</c>). Never anything private beyond what the text already says.</param>
/// <param name="CollapseId">Pushes sharing one replace each other on the lock screen instead of stacking.</param>
/// <param name="ExpiresAt">Apple stops trying to deliver after this; null means deliver once or not at all.</param>
public sealed record PushMessage(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    string? CollapseId = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>What became of one push to one phone.</summary>
public enum PushOutcome
{
    Delivered,
    /// <summary>Apple says this token is dead (app removed, phone wiped, wrong service): forget it.</summary>
    Unregistered,
    Failed,
    /// <summary>No APNs key is configured on this server; nothing was sent.</summary>
    NotConfigured,
}

/// <summary>Sends one push to one phone.</summary>
public interface IPushSender
{
    bool IsConfigured { get; }

    Task<PushOutcome> SendAsync(string token, PushEnvironment environment, PushMessage message, CancellationToken ct);
}
