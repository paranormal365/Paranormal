using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoAssetCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThumbnailUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    NativeWidth = table.Column<int>(type: "int", nullable: true),
                    NativeHeight = table.Column<int>(type: "int", nullable: true),
                    AllowRecolor = table.Column<bool>(type: "bit", nullable: false),
                    AllowResize = table.Column<bool>(type: "bit", nullable: false),
                    AllowOpacity = table.Column<bool>(type: "bit", nullable: false),
                    AllowRotation = table.Column<bool>(type: "bit", nullable: false),
                    AllowEffects = table.Column<bool>(type: "bit", nullable: false),
                    AllowEasing = table.Column<bool>(type: "bit", nullable: false),
                    AllowMotion = table.Column<bool>(type: "bit", nullable: false),
                    AllowControlPoints = table.Column<bool>(type: "bit", nullable: false),
                    PresetColors = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MinScale = table.Column<double>(type: "float", nullable: true),
                    MaxScale = table.Column<double>(type: "float", nullable: true),
                    FlattenOnExport = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoAssets_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoAssets_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoAssets_UploadFiles_ThumbnailUploadFileId",
                        column: x => x.ThumbnailUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoAssets_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_CreatedByAppUserId",
                table: "VideoAssets",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_IsActive_SortOrder",
                table: "VideoAssets",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_ThumbnailUploadFileId",
                table: "VideoAssets",
                column: "ThumbnailUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_UpdatedByAppUserId",
                table: "VideoAssets",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_UploadFileId",
                table: "VideoAssets",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoAssets");
        }
    }
}
