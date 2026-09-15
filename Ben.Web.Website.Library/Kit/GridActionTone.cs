namespace Ben.Web.Website.Library.Kit;

/// <summary>The colour a <c>BenGridAction</c> is outlined in: what kind of verb it is, not decoration.</summary>
public enum GridActionTone
{
    /// <summary>Edit, view, open — the everyday verbs.</summary>
    Neutral,

    /// <summary>Delete, remove, reject — undoing or losing something.</summary>
    Danger,

    /// <summary>Approve, accept, publish — saying yes.</summary>
    Success,

    /// <summary>The one action the row exists for, when there is one.</summary>
    Primary,

    /// <summary>Needs thought first: vote, impersonate, refund.</summary>
    Warning,
}
