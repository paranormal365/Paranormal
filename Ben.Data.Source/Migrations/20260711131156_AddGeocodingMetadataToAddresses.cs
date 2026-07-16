using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddGeocodingMetadataToAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeocodingResponseJson",
                table: "UserAddresses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeocodingResultType",
                table: "UserAddresses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeocodingResponseJson",
                table: "OrganizationAddresses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeocodingResultType",
                table: "OrganizationAddresses",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeocodingResponseJson",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "GeocodingResultType",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "GeocodingResponseJson",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "GeocodingResultType",
                table: "OrganizationAddresses");
        }
    }
}
