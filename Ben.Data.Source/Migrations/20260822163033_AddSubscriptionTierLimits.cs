using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTierLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionTierLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Limit = table.Column<int>(type: "int", nullable: false),
                    MaxValue = table.Column<int>(type: "int", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTierLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTierLimits_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierLimits_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierLimits_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierLimits_CreatedByAppUserId",
                table: "SubscriptionTierLimits",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierLimits_SubscriptionTierId_Limit",
                table: "SubscriptionTierLimits",
                columns: new[] { "SubscriptionTierId", "Limit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierLimits_UpdatedByAppUserId",
                table: "SubscriptionTierLimits",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionTierLimits");
        }
    }
}
