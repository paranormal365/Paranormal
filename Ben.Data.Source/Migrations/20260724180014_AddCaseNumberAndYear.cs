using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseNumberAndYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_OrganizationId",
                table: "Cases");

            migrationBuilder.AddColumn<int>(
                name: "CaseYear",
                table: "Cases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrgCaseNumber",
                table: "Cases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_OrganizationId_CaseYear_OrgCaseNumber",
                table: "Cases",
                columns: new[] { "OrganizationId", "CaseYear", "OrgCaseNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_OrganizationId_CaseYear_OrgCaseNumber",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CaseYear",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "OrgCaseNumber",
                table: "Cases");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_OrganizationId",
                table: "Cases",
                column: "OrganizationId");
        }
    }
}
