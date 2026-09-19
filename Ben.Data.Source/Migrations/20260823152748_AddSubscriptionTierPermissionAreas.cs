using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTierPermissionAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionTierPermissionAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Area = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTierPermissionAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPermissionAreas_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPermissionAreas_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPermissionAreas_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPermissionAreas_CreatedByAppUserId",
                table: "SubscriptionTierPermissionAreas",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPermissionAreas_SubscriptionTierId_Area",
                table: "SubscriptionTierPermissionAreas",
                columns: new[] { "SubscriptionTierId", "Area" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPermissionAreas_UpdatedByAppUserId",
                table: "SubscriptionTierPermissionAreas",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionTierPermissionAreas");
        }
    }
}
