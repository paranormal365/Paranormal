using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionContractTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionContractTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LimitsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionContractTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionContractTerms_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionContractTerms_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionContractTerms_OrganizationSubscriptions_OrganizationSubscriptionId",
                        column: x => x.OrganizationSubscriptionId,
                        principalTable: "OrganizationSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriptionContractTerms_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionContractTerms_CreatedByAppUserId",
                table: "SubscriptionContractTerms",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionContractTerms_OrganizationSubscriptionId_PeriodStartUtc",
                table: "SubscriptionContractTerms",
                columns: new[] { "OrganizationSubscriptionId", "PeriodStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionContractTerms_SubscriptionTierId",
                table: "SubscriptionContractTerms",
                column: "SubscriptionTierId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionContractTerms_UpdatedByAppUserId",
                table: "SubscriptionContractTerms",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionContractTerms");
        }
    }
}
