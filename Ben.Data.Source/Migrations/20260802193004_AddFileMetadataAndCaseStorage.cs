using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFileMetadataAndCaseStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "CaseTimelineEntries",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UploadFileMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    SampleRateHz = table.Column<int>(type: "int", nullable: true),
                    BitRateKbps = table.Column<int>(type: "int", nullable: true),
                    Channels = table.Column<int>(type: "int", nullable: true),
                    AudioCodec = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WidthPixels = table.Column<int>(type: "int", nullable: true),
                    HeightPixels = table.Column<int>(type: "int", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GpsLatitude = table.Column<double>(type: "float", nullable: true),
                    GpsLongitude = table.Column<double>(type: "float", nullable: true),
                    GpsAltitudeMeters = table.Column<double>(type: "float", nullable: true),
                    CameraManufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CameraModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RawMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileMetadata_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileMetadata_UploadFileId",
                table: "UploadFileMetadata",
                column: "UploadFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadFileMetadata");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "CaseTimelineEntries");
        }
    }
}
