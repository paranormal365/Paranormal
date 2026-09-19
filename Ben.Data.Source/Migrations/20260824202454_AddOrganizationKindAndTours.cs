using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationKindAndTours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RunsPublicTours",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_RunsPublicTours",
                table: "Organizations",
                column: "RunsPublicTours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_RunsPublicTours",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "RunsPublicTours",
                table: "Organizations");
        }
    }
}
