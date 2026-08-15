using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Replaces the timeline's binary <c>IsPublic</c> with a three-tier <c>Visibility</c>.
    /// </summary>
    /// <remarks>
    /// The scaffolded version of this dropped <c>IsPublic</c> first and added <c>Visibility</c>
    /// with a default of 0, which would have silently turned every published entry internal and
    /// emptied live public case pages. The order here is add → backfill → drop, so the old meaning
    /// survives: <c>true</c> becomes Public (2), <c>false</c> becomes OrgOnly (0).
    ///
    /// Nothing becomes Client (1) — that tier didn't exist before, so no historical entry can be
    /// claimed to have used it. Orgs opt entries into it going forward.
    /// </remarks>
    public partial class AddCaseTimelineVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "CaseTimelineEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE [CaseTimelineEntries] SET [Visibility] = 2 WHERE [IsPublic] = 1;");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "CaseTimelineEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "CaseTimelineEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Only Public maps back cleanly. Client-visible entries collapse to not-public, which
            // is the safe direction: rolling back must never publish something that wasn't public.
            migrationBuilder.Sql(
                "UPDATE [CaseTimelineEntries] SET [IsPublic] = 1 WHERE [Visibility] = 2;");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "CaseTimelineEntries");
        }
    }
}
