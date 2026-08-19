using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCheckoutPhotosAndRenewals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentCheckoutPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentCheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCheckoutPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutPhotos_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutPhotos_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutPhotos_EquipmentCheckouts_EquipmentCheckoutId",
                        column: x => x.EquipmentCheckoutId,
                        principalTable: "EquipmentCheckouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutPhotos_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentCheckoutRenewals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentCheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedDateDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateReviewed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCheckoutRenewals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutRenewals_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutRenewals_AppUsers_ReviewedByAppUserId",
                        column: x => x.ReviewedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutRenewals_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckoutRenewals_EquipmentCheckouts_EquipmentCheckoutId",
                        column: x => x.EquipmentCheckoutId,
                        principalTable: "EquipmentCheckouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutPhotos_CreatedByAppUserId",
                table: "EquipmentCheckoutPhotos",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutPhotos_EquipmentCheckoutId_Stage",
                table: "EquipmentCheckoutPhotos",
                columns: new[] { "EquipmentCheckoutId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutPhotos_UpdatedByAppUserId",
                table: "EquipmentCheckoutPhotos",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutPhotos_UploadFileId",
                table: "EquipmentCheckoutPhotos",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutRenewals_CreatedByAppUserId",
                table: "EquipmentCheckoutRenewals",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutRenewals_EquipmentCheckoutId_Status",
                table: "EquipmentCheckoutRenewals",
                columns: new[] { "EquipmentCheckoutId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutRenewals_ReviewedByAppUserId",
                table: "EquipmentCheckoutRenewals",
                column: "ReviewedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckoutRenewals_UpdatedByAppUserId",
                table: "EquipmentCheckoutRenewals",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentCheckoutPhotos");

            migrationBuilder.DropTable(
                name: "EquipmentCheckoutRenewals");
        }
    }
}
