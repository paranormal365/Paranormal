using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateEngagementCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivateEngagement",
                table: "Cases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WasPublicBeforeLapse",
                table: "Cases",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicLabel",
                table: "CaseRelatedPeople",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        
            // Backfill (idempotent): the private lane already exists in the data, it just was
            // never named. A case is a private engagement when it was born from a client's
            // request, or when it (or one of its investigations) is bound to a private-residence
            // place. PlaceKind.PrivateResidence = 1.
            migrationBuilder.Sql("""
                UPDATE Cases SET IsPrivateEngagement = 1 WHERE ClientRequestId IS NOT NULL;

                UPDATE c SET c.IsPrivateEngagement = 1
                FROM Cases c JOIN Places p ON p.Id = c.PlaceId
                WHERE p.Kind = 1;

                UPDATE c SET c.IsPrivateEngagement = 1
                FROM Cases c
                WHERE EXISTS (
                    SELECT 1 FROM Investigations i
                    JOIN Places p ON p.Id = i.PlaceId
                    WHERE i.CaseId = c.Id AND p.Kind = 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPrivateEngagement",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "WasPublicBeforeLapse",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "PublicLabel",
                table: "CaseRelatedPeople");
        }
    }
}
