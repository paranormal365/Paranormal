using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventRunMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalledOffNewDate",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalledOffRefundHow",
                table: "HostedEvents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventRunMonths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PriceAtPurchase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PurchasedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryWarningSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedToHostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GrantedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefundedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderCheckoutRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderPaymentRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRunMonths", x => x.Id);
                    table.CheckConstraint("CK_EventRunMonths_OneOwner", "([OwnerOrganizationId] IS NOT NULL AND [OwnerAppUserId] IS NULL) OR ([OwnerOrganizationId] IS NULL AND [OwnerAppUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EventRunMonths_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventRunMonths_AppUsers_OwnerAppUserId",
                        column: x => x.OwnerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventRunMonths_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventRunMonths_HostedEvents_AppliedToHostedEventId",
                        column: x => x.AppliedToHostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EventRunMonths_Organizations_OwnerOrganizationId",
                        column: x => x.OwnerOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_AppliedToHostedEventId_RefundedUtc",
                table: "EventRunMonths",
                columns: new[] { "AppliedToHostedEventId", "RefundedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_CreatedByAppUserId",
                table: "EventRunMonths",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_OwnerAppUserId_AppliedToHostedEventId_ExpiresUtc",
                table: "EventRunMonths",
                columns: new[] { "OwnerAppUserId", "AppliedToHostedEventId", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_OwnerOrganizationId_AppliedToHostedEventId_ExpiresUtc",
                table: "EventRunMonths",
                columns: new[] { "OwnerOrganizationId", "AppliedToHostedEventId", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_ProviderPaymentRef",
                table: "EventRunMonths",
                column: "ProviderPaymentRef",
                filter: "[ProviderPaymentRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventRunMonths_UpdatedByAppUserId",
                table: "EventRunMonths",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRunMonths");

            migrationBuilder.DropColumn(
                name: "CalledOffNewDate",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "CalledOffRefundHow",
                table: "HostedEvents");
        }
    }
}
