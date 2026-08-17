using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicEventSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "OrgCalendarEvents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_OrganizationId_UrlName",
                table: "OrgCalendarEvents",
                columns: new[] { "OrganizationId", "UrlName" },
                unique: true,
                filter: "[UrlName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_OrganizationId_UrlName",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "OrgCalendarEvents");
        }
    }
}
