using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BorrowerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BorrowedForOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateReviewed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateNeededFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCheckedOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedOutConfirmedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateReturned = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedReceivedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnConditionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCheckouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_AppUsers_BorrowerAppUserId",
                        column: x => x.BorrowerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_AppUsers_ReviewedByAppUserId",
                        column: x => x.ReviewedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_EquipmentItems_EquipmentItemId",
                        column: x => x.EquipmentItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCheckouts_Organizations_BorrowedForOrganizationId",
                        column: x => x.BorrowedForOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_BorrowedForOrganizationId_Status",
                table: "EquipmentCheckouts",
                columns: new[] { "BorrowedForOrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_BorrowerAppUserId",
                table: "EquipmentCheckouts",
                column: "BorrowerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_CreatedByAppUserId",
                table: "EquipmentCheckouts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_EquipmentItemId_Status",
                table: "EquipmentCheckouts",
                columns: new[] { "EquipmentItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_InvestigationId",
                table: "EquipmentCheckouts",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_ReviewedByAppUserId",
                table: "EquipmentCheckouts",
                column: "ReviewedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCheckouts_UpdatedByAppUserId",
                table: "EquipmentCheckouts",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentCheckouts");
        }
    }
}
