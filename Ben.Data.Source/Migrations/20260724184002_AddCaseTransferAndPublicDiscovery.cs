using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseTransferAndPublicDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseTransferLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RespondedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TransferReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DateProposed = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateResponded = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTransferLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseTransferLogs_AppUsers_ProposedByAppUserId",
                        column: x => x.ProposedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTransferLogs_AppUsers_RespondedByAppUserId",
                        column: x => x.RespondedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTransferLogs_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTransferLogs_Organizations_FromOrganizationId",
                        column: x => x.FromOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTransferLogs_Organizations_ToOrganizationId",
                        column: x => x.ToOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_CaseId",
                table: "CaseTransferLogs",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_FromOrganizationId",
                table: "CaseTransferLogs",
                column: "FromOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_ProposedByAppUserId",
                table: "CaseTransferLogs",
                column: "ProposedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_RespondedByAppUserId",
                table: "CaseTransferLogs",
                column: "RespondedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_ToOrganizationId",
                table: "CaseTransferLogs",
                column: "ToOrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseTransferLogs");
        }
    }
}
