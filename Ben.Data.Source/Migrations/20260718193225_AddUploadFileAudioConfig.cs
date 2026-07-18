using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileAudioConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadFileAudioConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WaveColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProgressColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CursorColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CursorWidth = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    BarWidth = table.Column<int>(type: "int", nullable: true),
                    BarGap = table.Column<int>(type: "int", nullable: true),
                    BarRadius = table.Column<int>(type: "int", nullable: true),
                    BarHeight = table.Column<double>(type: "float", nullable: true),
                    BarAlign = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Normalize = table.Column<bool>(type: "bit", nullable: false),
                    DragToSeek = table.Column<bool>(type: "bit", nullable: false),
                    HideScrollbar = table.Column<bool>(type: "bit", nullable: false),
                    AudioRate = table.Column<double>(type: "float", nullable: true),
                    EnableHover = table.Column<bool>(type: "bit", nullable: false),
                    EnableTimeline = table.Column<bool>(type: "bit", nullable: false),
                    EnableZoom = table.Column<bool>(type: "bit", nullable: false),
                    EnableMinimap = table.Column<bool>(type: "bit", nullable: false),
                    EnableSpectrogram = table.Column<bool>(type: "bit", nullable: false),
                    EnableSpectrogramWindowed = table.Column<bool>(type: "bit", nullable: false),
                    EnableEnvelope = table.Column<bool>(type: "bit", nullable: false),
                    EnableRegions = table.Column<bool>(type: "bit", nullable: false),
                    HoverOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimelineOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZoomOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MinimapOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpectrogramOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpectrogramWindowedOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnvelopeOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitialHeight = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MinHeight = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxHeight = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShowControls = table.Column<bool>(type: "bit", nullable: false),
                    MinZoom = table.Column<double>(type: "float", nullable: false),
                    MaxZoom = table.Column<double>(type: "float", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileAudioConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileAudioConfigs_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileAudioConfigs_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileAudioConfigs_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileAudioConfigs_CreatedByAppUserId",
                table: "UploadFileAudioConfigs",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileAudioConfigs_UpdatedByAppUserId",
                table: "UploadFileAudioConfigs",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileAudioConfigs_UploadFileId",
                table: "UploadFileAudioConfigs",
                column: "UploadFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadFileAudioConfigs");
        }
    }
}
