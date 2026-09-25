using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class PlaceDescriptionAndMediaKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaceEvidence_PlaceId_ReviewState_DateCreated",
                table: "PlaceEvidence");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Places",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaKind",
                table: "PlaceEvidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_PlaceId_MediaKind_ReviewState_DateCreated",
                table: "PlaceEvidence",
                columns: new[] { "PlaceId", "MediaKind", "ReviewState", "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaceEvidence_PlaceId_MediaKind_ReviewState_DateCreated",
                table: "PlaceEvidence");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "MediaKind",
                table: "PlaceEvidence");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_PlaceId_ReviewState_DateCreated",
                table: "PlaceEvidence",
                columns: new[] { "PlaceId", "ReviewState", "DateCreated" });
        }
    }
}
