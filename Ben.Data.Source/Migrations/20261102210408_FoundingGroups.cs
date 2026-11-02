using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FoundingGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReferredAtUtc",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferredByOrganizationId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationFreeMonths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EarnedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedToPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PromotionEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationFreeMonths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationFreeMonths_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationFreeMonths_Organizations_SourceOrganizationId",
                        column: x => x.SourceOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    OpenedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimWindowDays = table.Column<int>(type: "int", nullable: false),
                    Spots = table.Column<int>(type: "int", nullable: false),
                    GatherDays = table.Column<int>(type: "int", nullable: false),
                    MinimumMembers = table.Column<int>(type: "int", nullable: false),
                    FreeMonths = table.Column<int>(type: "int", nullable: false),
                    Winners = table.Column<int>(type: "int", nullable: false),
                    WinnerMonths = table.Column<int>(type: "int", nullable: false),
                    GroupOfTheMonthMonths = table.Column<int>(type: "int", nullable: false),
                    ReachedScoreMonths = table.Column<int>(type: "int", nullable: false),
                    ReachableScorePerMember = table.Column<int>(type: "int", nullable: false),
                    PointsPerSession = table.Column<int>(type: "int", nullable: false),
                    PointsPerInvestigation = table.Column<int>(type: "int", nullable: false),
                    PointsPerEvent = table.Column<int>(type: "int", nullable: false),
                    PointsPerEvidence = table.Column<int>(type: "int", nullable: false),
                    PointsPerReferral = table.Column<int>(type: "int", nullable: false),
                    MinimumReadingsPerSession = table.Column<int>(type: "int", nullable: false),
                    GroupOfTheMonthDecidedThroughUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromotionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GatherEndsUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QualifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FreeYearEndsUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MembersAtQualifying = table.Column<int>(type: "int", nullable: false),
                    SubscriptionTierIdEarned = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalScorePerMember = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FinalScoreTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FinalRank = table.Column<int>(type: "int", nullable: true),
                    DisqualifiedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisqualifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DayLeftSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    YearEndMonthNoticeSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    YearEndWeekNoticeSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionEntries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionEntries_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionEntries_SubscriptionTiers_SubscriptionTierIdEarned",
                        column: x => x.SubscriptionTierIdEarned,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionMembers_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionMembers_PromotionEntries_PromotionEntryId",
                        column: x => x.PromotionEntryId,
                        principalTable: "PromotionEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_ReferredByOrganizationId",
                table: "Organizations",
                column: "ReferredByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFreeMonths_Key",
                table: "OrganizationFreeMonths",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFreeMonths_OrganizationId_AppliedUtc",
                table: "OrganizationFreeMonths",
                columns: new[] { "OrganizationId", "AppliedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFreeMonths_SourceOrganizationId",
                table: "OrganizationFreeMonths",
                column: "SourceOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionEntries_OrganizationId",
                table: "PromotionEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionEntries_PromotionId_OrganizationId",
                table: "PromotionEntries",
                columns: new[] { "PromotionId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionEntries_PromotionId_State",
                table: "PromotionEntries",
                columns: new[] { "PromotionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionEntries_SubscriptionTierIdEarned",
                table: "PromotionEntries",
                column: "SubscriptionTierIdEarned");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionMembers_AppUserId",
                table: "PromotionMembers",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionMembers_PromotionEntryId",
                table: "PromotionMembers",
                column: "PromotionEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionMembers_PromotionId_AppUserId",
                table: "PromotionMembers",
                columns: new[] { "PromotionId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Key",
                table: "Promotions",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationFreeMonths");

            migrationBuilder.DropTable(
                name: "PromotionMembers");

            migrationBuilder.DropTable(
                name: "PromotionEntries");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_ReferredByOrganizationId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ReferredAtUtc",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ReferredByOrganizationId",
                table: "Organizations");
        }
    }
}
