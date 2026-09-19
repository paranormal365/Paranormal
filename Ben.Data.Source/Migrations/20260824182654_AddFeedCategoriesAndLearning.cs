using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedCategoriesAndLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CategoryMatchScore",
                table: "OrgMessages",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedExperienceTypeId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeedLabelledExamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExperienceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedLabelledExamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedLabelledExamples_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FeedLabelledExamples_ExperienceTypes_ExperienceTypeId",
                        column: x => x.ExperienceTypeId,
                        principalTable: "ExperienceTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FeedLabelledExamples_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FeedMediaFeatureSets",
                columns: table => new
                {
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsVideo = table.Column<bool>(type: "bit", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    HasAudio = table.Column<bool>(type: "bit", nullable: true),
                    WidthPixels = table.Column<int>(type: "int", nullable: true),
                    HeightPixels = table.Column<int>(type: "int", nullable: true),
                    MeanLuma = table.Column<double>(type: "float", nullable: true),
                    LumaStdDev = table.Column<double>(type: "float", nullable: true),
                    CapturedHourLocal = table.Column<int>(type: "int", nullable: true),
                    CameraManufacturer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AudioAnomalyScore = table.Column<double>(type: "float", nullable: true),
                    EvpHitCount = table.Column<int>(type: "int", nullable: true),
                    MotionScore = table.Column<double>(type: "float", nullable: true),
                    ExtraJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMediaFeatureSets", x => x.OrgMessageId);
                    table.ForeignKey(
                        name: "FK_FeedMediaFeatureSets_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedTypeWeightSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FitVersion = table.Column<int>(type: "int", nullable: false),
                    FitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExampleCount = table.Column<int>(type: "int", nullable: false),
                    WeightsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HoldoutAccuracy = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedTypeWeightSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedTypeWeightSets_ExperienceTypes_ExperienceTypeId",
                        column: x => x.ExperienceTypeId,
                        principalTable: "ExperienceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_FeedExperienceTypeId_DateCreated",
                table: "OrgMessages",
                columns: new[] { "FeedExperienceTypeId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedLabelledExamples_DecidedByAppUserId",
                table: "FeedLabelledExamples",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedLabelledExamples_ExperienceTypeId_DecidedUtc",
                table: "FeedLabelledExamples",
                columns: new[] { "ExperienceTypeId", "DecidedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedLabelledExamples_OrgMessageId",
                table: "FeedLabelledExamples",
                column: "OrgMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedTypeWeightSets_ExperienceTypeId_FitVersion",
                table: "FeedTypeWeightSets",
                columns: new[] { "ExperienceTypeId", "FitVersion" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_ExperienceTypes_FeedExperienceTypeId",
                table: "OrgMessages",
                column: "FeedExperienceTypeId",
                principalTable: "ExperienceTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_ExperienceTypes_FeedExperienceTypeId",
                table: "OrgMessages");

            migrationBuilder.DropTable(
                name: "FeedLabelledExamples");

            migrationBuilder.DropTable(
                name: "FeedMediaFeatureSets");

            migrationBuilder.DropTable(
                name: "FeedTypeWeightSets");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_FeedExperienceTypeId_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "CategoryMatchScore",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "FeedExperienceTypeId",
                table: "OrgMessages");
        }
    }
}
