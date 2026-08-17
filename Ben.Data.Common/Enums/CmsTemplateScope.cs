namespace Ben.Data.Common.Enums
{
    /// <summary>How much of a page a saved template covers.</summary>
    /// <remarks>
    /// Two granularities because Ben described both: a group can save one section it has built to
    /// reuse elsewhere, or a whole page's worth of sections as a starting point for the next one.
    /// They are the same idea at different sizes, so one table carries both rather than two that
    /// would drift.
    /// </remarks>
    public enum CmsTemplateScope
    {
        /// <summary>One section — its type and its content.</summary>
        Section = 0,

        /// <summary>An ordered set of sections, used to start a new page.</summary>
        Page = 1,
    }
}
