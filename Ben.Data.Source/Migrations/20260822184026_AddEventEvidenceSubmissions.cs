using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEventEvidenceSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventEvidenceSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateReviewed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventEvidenceSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_AppUsers_ReviewedByAppUserId",
                        column: x => x.ReviewedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_AppUsers_SubmittedByAppUserId",
                        column: x => x.SubmittedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventEvidenceSubmissions_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_CreatedByAppUserId",
                table: "EventEvidenceSubmissions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_OrgCalendarEventId_Status",
                table: "EventEvidenceSubmissions",
                columns: new[] { "OrgCalendarEventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_ReviewedByAppUserId",
                table: "EventEvidenceSubmissions",
                column: "ReviewedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_SubmittedByAppUserId",
                table: "EventEvidenceSubmissions",
                column: "SubmittedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_UpdatedByAppUserId",
                table: "EventEvidenceSubmissions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidenceSubmissions_UploadFileId",
                table: "EventEvidenceSubmissions",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventEvidenceSubmissions");
        }
    }
}
