namespace Ben.Data.Common.Enums;

public enum CaseReportSectionType
{
    Text        = 0,  // free-form HTML narrative
    Evidence    = 1,  // uploaded files with player/viewer
    Timeline    = 2,  // selected case timeline entries
    Occurrences = 3,  // selected client-reported occurrences

    /// <summary>Field sessions recorded on a phone or iPad and uploaded to the site — readings,
    /// marks, positions and the recordings that go with them.</summary>
    FieldSessions = 4,
}
