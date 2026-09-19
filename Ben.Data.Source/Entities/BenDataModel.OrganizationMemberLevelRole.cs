using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A role that a title <i>usually</i> carries — offered when the title is assigned, never
    /// applied on its own (item 156 step 5).
    /// </summary>
    /// <remarks>
    /// <para><b>A suggestion, not an inheritance.</b> <see cref="OrganizationMemberLevel"/> states
    /// the rule this must not break: a title is seniority, never permission, and no code may read
    /// it to decide access. So this table is never consulted when answering "may they?" — it is
    /// read at exactly one moment, when an administrator assigns a title, to offer the roles that
    /// usually go with it. Accepting the offer writes ordinary
    /// <see cref="OrganizationRoleMembership"/> rows, and from then on those rows are the whole
    /// truth.</para>
    ///
    /// <para><b>Why copy rather than inherit.</b> Live inheritance would make every promotion a
    /// silent grant and every edit of the ladder a silent re-grant across the group — access
    /// changing for people nobody was looking at, from a screen labelled "titles". Copying keeps
    /// the audit honest: somebody chose, on a date, to give this person these roles. Editing the
    /// suggestions afterwards changes what the NEXT assignment offers and nothing that already
    /// happened.</para>
    ///
    /// <para>Cascade from the rung, because a suggestion attached to a deleted rung is nothing.
    /// Deleting a rung still cannot take anyone's access away: the role memberships it once
    /// suggested are independent rows and stay exactly as they are.</para>
    /// </remarks>
    public partial class OrganizationMemberLevelRole : IIDStd
    {
        public Guid Id { get; set; }
    }
}
