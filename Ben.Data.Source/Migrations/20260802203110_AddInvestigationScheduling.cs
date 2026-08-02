using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestigationScheduleProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AcceptedSlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientCounterDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientResponseNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClientRespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationScheduleProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationScheduleProposals_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationScheduleProposals_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationScheduleProposals_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationScheduleProposals_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleProposalSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleProposalSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleProposalSlots_InvestigationScheduleProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "InvestigationScheduleProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationScheduleProposals_CaseId_Status",
                table: "InvestigationScheduleProposals",
                columns: new[] { "CaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationScheduleProposals_CreatedByAppUserId",
                table: "InvestigationScheduleProposals",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationScheduleProposals_InvestigationId",
                table: "InvestigationScheduleProposals",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationScheduleProposals_UpdatedByAppUserId",
                table: "InvestigationScheduleProposals",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleProposalSlots_ProposalId",
                table: "ScheduleProposalSlots",
                column: "ProposalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleProposalSlots");

            migrationBuilder.DropTable(
                name: "InvestigationScheduleProposals");
        }
    }
}
