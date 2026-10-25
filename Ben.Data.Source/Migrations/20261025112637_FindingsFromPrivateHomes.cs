using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FindingsFromPrivateHomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 10/25/2026: accepted evidence at an event held at a private home was made public on acceptance.
            // Those files go back to private; the event page now serves them only to the people who were there.
            migrationBuilder.Sql("""
                UPDATE f SET f.IsPublic = 0
                FROM UploadFiles f
                JOIN EventEvidenceSubmissions s ON s.UploadFileId = f.Id
                JOIN OrgCalendarEvents e ON e.Id = s.OrgCalendarEventId
                JOIN Places p ON p.Id = e.PlaceId
                WHERE p.Kind = 1 AND f.IsPublic = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: making a private home's findings public again is not a rollback anybody wants.
        }
    }
}
