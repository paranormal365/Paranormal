using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentLoanFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentLoanFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentCheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    CounterpartyComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    ProductComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubjectAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentLoanFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_AppUsers_SubjectAppUserId",
                        column: x => x.SubjectAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_EquipmentCheckouts_EquipmentCheckoutId",
                        column: x => x.EquipmentCheckoutId,
                        principalTable: "EquipmentCheckouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentLoanFeedbacks_Organizations_SubjectOrganizationId",
                        column: x => x.SubjectOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_AuthorAppUserId",
                table: "EquipmentLoanFeedbacks",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_CreatedByAppUserId",
                table: "EquipmentLoanFeedbacks",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_EquipmentCheckoutId_Role",
                table: "EquipmentLoanFeedbacks",
                columns: new[] { "EquipmentCheckoutId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_SubjectAppUserId_Role",
                table: "EquipmentLoanFeedbacks",
                columns: new[] { "SubjectAppUserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_SubjectOrganizationId_Role",
                table: "EquipmentLoanFeedbacks",
                columns: new[] { "SubjectOrganizationId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoanFeedbacks_UpdatedByAppUserId",
                table: "EquipmentLoanFeedbacks",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentLoanFeedbacks");
        }
    }
}
