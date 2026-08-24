using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingLedgerAndTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReferrerAppUserId",
                table: "Coupons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferrerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AdjustmentIsCredit = table.Column<bool>(type: "bit", nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReceiptNumber = table.Column<int>(type: "int", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingLedgerEntries_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillingLedgerEntries_AppUsers_ReferrerAppUserId",
                        column: x => x.ReferrerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingLedgerEntries_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillingLedgerEntries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRateRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxRateRules_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxRateRules_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_ReferrerAppUserId",
                table: "Coupons",
                column: "ReferrerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_CreatedByAppUserId",
                table: "BillingLedgerEntries",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_OrganizationId_DateCreated",
                table: "BillingLedgerEntries",
                columns: new[] { "OrganizationId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_ReceiptNumber",
                table: "BillingLedgerEntries",
                column: "ReceiptNumber",
                unique: true,
                filter: "[ReceiptNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_ReferrerAppUserId",
                table: "BillingLedgerEntries",
                column: "ReferrerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_UpdatedByAppUserId",
                table: "BillingLedgerEntries",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRules_CreatedByAppUserId",
                table: "TaxRateRules",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRules_State",
                table: "TaxRateRules",
                column: "State",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRules_UpdatedByAppUserId",
                table: "TaxRateRules",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_AppUsers_ReferrerAppUserId",
                table: "Coupons",
                column: "ReferrerAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_AppUsers_ReferrerAppUserId",
                table: "Coupons");

            migrationBuilder.DropTable(
                name: "BillingLedgerEntries");

            migrationBuilder.DropTable(
                name: "TaxRateRules");

            migrationBuilder.DropIndex(
                name: "IX_Coupons_ReferrerAppUserId",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "ReferrerAppUserId",
                table: "Coupons");
        }
    }
}
