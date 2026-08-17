using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "Cases",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_OrganizationId_UrlName",
                table: "Cases",
                columns: new[] { "OrganizationId", "UrlName" },
                unique: true,
                filter: "[UrlName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_OrganizationId_UrlName",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "Cases");
        }
    }
}
