using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationAndEvidenceVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenceVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoterAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoterOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoteType = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsPublicVoter = table.Column<bool>(type: "bit", nullable: false),
                    DateVoted = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceVotes_AppUsers_VoterAppUserId",
                        column: x => x.VoterAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EvidenceVotes_Organizations_VoterOrganizationId",
                        column: x => x.VoterOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EvidenceVotes_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Investigations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ScheduledDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Investigations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Investigations_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Investigations_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Investigations_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Investigations_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InvestigationAttendees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedRole = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DidAttend = table.Column<bool>(type: "bit", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationAttendees_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationAttendees_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationAttendees_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVotes_UploadFileId_VoterAppUserId",
                table: "EvidenceVotes",
                columns: new[] { "UploadFileId", "VoterAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVotes_VoterAppUserId",
                table: "EvidenceVotes",
                column: "VoterAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVotes_VoterOrganizationId",
                table: "EvidenceVotes",
                column: "VoterOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationAttendees_AppUserId",
                table: "InvestigationAttendees",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationAttendees_CreatedByAppUserId",
                table: "InvestigationAttendees",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationAttendees_InvestigationId_AppUserId",
                table: "InvestigationAttendees",
                columns: new[] { "InvestigationId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_CaseId",
                table: "Investigations",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_CreatedByAppUserId",
                table: "Investigations",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_OrgCalendarEventId",
                table: "Investigations",
                column: "OrgCalendarEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_UpdatedByAppUserId",
                table: "Investigations",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvidenceVotes");

            migrationBuilder.DropTable(
                name: "InvestigationAttendees");

            migrationBuilder.DropTable(
                name: "Investigations");
        }
    }
}
