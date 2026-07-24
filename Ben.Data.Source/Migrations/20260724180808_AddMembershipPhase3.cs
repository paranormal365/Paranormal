using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPhase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanReapply",
                table: "OrganizationMembershipRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenialReason",
                table: "OrganizationMembershipRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnderReview",
                table: "OrganizationMembershipRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoteDeadline",
                table: "OrganizationMembershipRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MembershipReviewVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMembershipRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoterAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoteType = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DateVoted = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipReviewVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipReviewVotes_AppUsers_VoterAppUserId",
                        column: x => x.VoterAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MembershipReviewVotes_OrganizationMembershipRequests_OrganizationMembershipRequestId",
                        column: x => x.OrganizationMembershipRequestId,
                        principalTable: "OrganizationMembershipRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembershipQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembershipQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipQuestions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipQuestions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipQuestions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembershipAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMembershipRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMembershipQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembershipAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipAnswers_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipAnswers_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipAnswers_OrganizationMembershipQuestions_OrganizationMembershipQuestionId",
                        column: x => x.OrganizationMembershipQuestionId,
                        principalTable: "OrganizationMembershipQuestions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembershipAnswers_OrganizationMembershipRequests_OrganizationMembershipRequestId",
                        column: x => x.OrganizationMembershipRequestId,
                        principalTable: "OrganizationMembershipRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipReviewVotes_OrganizationMembershipRequestId_VoterAppUserId",
                table: "MembershipReviewVotes",
                columns: new[] { "OrganizationMembershipRequestId", "VoterAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipReviewVotes_VoterAppUserId",
                table: "MembershipReviewVotes",
                column: "VoterAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipAnswers_CreatedByAppUserId",
                table: "OrganizationMembershipAnswers",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipAnswers_OrganizationMembershipQuestionId",
                table: "OrganizationMembershipAnswers",
                column: "OrganizationMembershipQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipAnswers_OrganizationMembershipRequestId_OrganizationMembershipQuestionId",
                table: "OrganizationMembershipAnswers",
                columns: new[] { "OrganizationMembershipRequestId", "OrganizationMembershipQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipAnswers_UpdatedByAppUserId",
                table: "OrganizationMembershipAnswers",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipQuestions_CreatedByAppUserId",
                table: "OrganizationMembershipQuestions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipQuestions_OrganizationId",
                table: "OrganizationMembershipQuestions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipQuestions_UpdatedByAppUserId",
                table: "OrganizationMembershipQuestions",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MembershipReviewVotes");

            migrationBuilder.DropTable(
                name: "OrganizationMembershipAnswers");

            migrationBuilder.DropTable(
                name: "OrganizationMembershipQuestions");

            migrationBuilder.DropColumn(
                name: "CanReapply",
                table: "OrganizationMembershipRequests");

            migrationBuilder.DropColumn(
                name: "DenialReason",
                table: "OrganizationMembershipRequests");

            migrationBuilder.DropColumn(
                name: "IsUnderReview",
                table: "OrganizationMembershipRequests");

            migrationBuilder.DropColumn(
                name: "VoteDeadline",
                table: "OrganizationMembershipRequests");
        }
    }
}
