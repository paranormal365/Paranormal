using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Takes back the Investigator Role that a seeder handed to every member.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's decision, 2026-08-26:</b> "Currently I am the only actual person using the
    /// site. Keep me as the super admin then change the security settings instead of
    /// grandfathering anyone."</para>
    ///
    /// <para>Until now <c>OrgRoleSeeder</c> created an Investigator Role (Cases + Investigations
    /// Read) and assigned it to every active non-admin member, so item 156 Phase D's enforcement
    /// flip took nothing from anyone. The seeder no longer does that, but the rows it already
    /// wrote outlive it — and while they exist a read grant can still only ADD, never restrict,
    /// which is exactly what IH-03 was about.</para>
    ///
    /// <para><b>Targeted by the seeder's own description, not by role name.</b> A group may well
    /// have an Investigator Role somebody created deliberately, and that one must survive. Only
    /// rows whose role carries the exact sentence the seeder wrote are touched.</para>
    ///
    /// <para><b>The role itself is kept</b>, with its permissions, and only emptied of members —
    /// so it is sitting there ready to hand to whoever should have it. Its description is
    /// rewritten, because the old one describes a grandfathering that no longer happens and would
    /// mislead the next person to read it.</para>
    ///
    /// <para><b>Down cannot restore this.</b> Which membership rows existed is not recoverable
    /// from what remains, and inventing them would hand out access nobody granted. Reversing this
    /// means assigning the role to the people who should hold it, which is a decision rather than
    /// a rollback.</para>
    /// </remarks>
    public partial class RevokeGrandfatheredInvestigatorRole : Migration
    {
        private const string SeededDescription =
            "Reads the group's cases and investigations. Assigned to everyone "
            + "who was already a member when role-based case access arrived, so the "
            + "change took nothing from anyone; hand it to new members as they earn it.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM [OrganizationRoleMemberships]
                WHERE [OrganizationRoleId] IN (
                    SELECT [Id] FROM [OrganizationRoles]
                    WHERE [Name] = N'Investigator Role'
                      AND [Description] = N'{SeededDescription.Replace("'", "''")}'
                );");

            // The role stays, ready to be given deliberately — but it should stop describing a
            // grandfathering that no longer happens.
            migrationBuilder.Sql($@"
                UPDATE [OrganizationRoles]
                SET [Description] = N'Reads the group''s cases and investigations. Assign it to the members who should see them.'
                WHERE [Name] = N'Investigator Role'
                  AND [Description] = N'{SeededDescription.Replace("'", "''")}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. See the remarks: which members held the role is not recoverable,
            // and guessing would grant access nobody chose to give.
        }
    }
}
