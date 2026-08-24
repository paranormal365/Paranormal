using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOverflowSeatsAndReferralCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricePerExtraMember",
                table: "SubscriptionTierPrices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferralCommissionPercent",
                table: "Coupons",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberSeatSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    PriceAtStart = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberSeatSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberSeatSubscriptions_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MemberSeatSubscriptions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MemberSeatSubscriptions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MemberSeatSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberSeatSubscriptions_AppUserId",
                table: "MemberSeatSubscriptions",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberSeatSubscriptions_CreatedByAppUserId",
                table: "MemberSeatSubscriptions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberSeatSubscriptions_OrganizationId_AppUserId",
                table: "MemberSeatSubscriptions",
                columns: new[] { "OrganizationId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberSeatSubscriptions_UpdatedByAppUserId",
                table: "MemberSeatSubscriptions",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberSeatSubscriptions");

            migrationBuilder.DropColumn(
                name: "PricePerExtraMember",
                table: "SubscriptionTierPrices");

            migrationBuilder.DropColumn(
                name: "ReferralCommissionPercent",
                table: "Coupons");
        }
    }
}
