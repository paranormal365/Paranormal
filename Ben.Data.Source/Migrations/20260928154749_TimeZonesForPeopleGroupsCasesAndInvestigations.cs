using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class TimeZonesForPeopleGroupsCasesAndInvestigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Organizations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "America/Chicago");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Investigations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Cases",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AppUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Investigations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AppUsers");
        }
    }
}
