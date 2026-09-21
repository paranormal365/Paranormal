namespace Ben.Service.Models.Entities;

/// <summary>One letter somebody may turn off, and whether they have.</summary>
public record EmailPreferenceRecord
{
    /// <summary>The letter's key, as MailKinds declares it.</summary>
    public required string Kind { get; init; }

    /// <summary>What the letter is, in the words the preferences screen shows.</summary>
    public required string Title { get; init; }

    /// <summary>When it is sent, so somebody can tell whether they want it.</summary>
    public required string Description { get; init; }

    /// <summary>True when they receive it — which is the default for every letter.</summary>
    public bool Wanted { get; init; }
}
