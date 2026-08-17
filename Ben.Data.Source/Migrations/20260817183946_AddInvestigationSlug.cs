using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "Investigations",
                type: "nvarchar(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_OrganizationId_UrlName",
                table: "Investigations",
                columns: new[] { "OrganizationId", "UrlName" },
                unique: true,
                filter: "[UrlName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Investigations_OrganizationId_UrlName",
                table: "Investigations");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "Investigations");
        }
    }
}
