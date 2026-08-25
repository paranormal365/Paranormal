using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedAttributionAndConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttributedOrganizationId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttributionDecidedByAppUserId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttributionDecidedUtc",
                table: "OrgMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttributionState",
                table: "OrgMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FeedPostConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgreedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgreedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WordingVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedPostConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedPostConsents_AppUsers_AgreedByAppUserId",
                        column: x => x.AgreedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FeedPostConsents_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FeedPostConsents_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_AttributedOrganizationId_AttributionState_DateCreated",
                table: "OrgMessages",
                columns: new[] { "AttributedOrganizationId", "AttributionState", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedPostConsents_AgreedByAppUserId",
                table: "FeedPostConsents",
                column: "AgreedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedPostConsents_CaseId_AgreedUtc",
                table: "FeedPostConsents",
                columns: new[] { "CaseId", "AgreedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedPostConsents_OrgMessageId",
                table: "FeedPostConsents",
                column: "OrgMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_Organizations_AttributedOrganizationId",
                table: "OrgMessages",
                column: "AttributedOrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_Organizations_AttributedOrganizationId",
                table: "OrgMessages");

            migrationBuilder.DropTable(
                name: "FeedPostConsents");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_AttributedOrganizationId_AttributionState_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "AttributedOrganizationId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "AttributionDecidedByAppUserId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "AttributionDecidedUtc",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "AttributionState",
                table: "OrgMessages");
        }
    }
}
