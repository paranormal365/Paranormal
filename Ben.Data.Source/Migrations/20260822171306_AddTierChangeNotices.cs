using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddTierChangeNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TierChangeNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sentences = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliverAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TierChangeNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TierChangeNotices_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TierChangeNotices_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TierChangeNotices_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TierChangeNotices_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TierChangeNotices_CreatedByAppUserId",
                table: "TierChangeNotices",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TierChangeNotices_DeliveredAtUtc_DeliverAtUtc",
                table: "TierChangeNotices",
                columns: new[] { "DeliveredAtUtc", "DeliverAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TierChangeNotices_OrganizationId",
                table: "TierChangeNotices",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TierChangeNotices_SubscriptionTierId",
                table: "TierChangeNotices",
                column: "SubscriptionTierId");

            migrationBuilder.CreateIndex(
                name: "IX_TierChangeNotices_UpdatedByAppUserId",
                table: "TierChangeNotices",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TierChangeNotices");
        }
    }
}
