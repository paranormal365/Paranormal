using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSessionFirstFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "FieldSessionUploads",
                type: "decimal(18,10)",
                precision: 18,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "FieldSessionUploads",
                type: "decimal(18,10)",
                precision: 18,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PositionResolved",
                table: "FieldSessionUploads",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_SubmittedByAppUserId_PositionResolved_Latitude_Longitude",
                table: "FieldSessionUploads",
                columns: new[] { "SubmittedByAppUserId", "PositionResolved", "Latitude", "Longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_SubmittedByAppUserId_PositionResolved_Latitude_Longitude",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "PositionResolved",
                table: "FieldSessionUploads");
        }
    }
}
