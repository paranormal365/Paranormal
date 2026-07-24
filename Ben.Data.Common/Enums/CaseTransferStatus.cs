namespace Ben.Data.Common.Enums;

/// <summary>Status of a case transfer proposal from one org to another.</summary>
public enum CaseTransferStatus
{
    Pending  = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3,
}
