using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationDutiesAndCaseContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseContacts_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseContacts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseContacts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseContacts_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvestigationDuties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSingleHolder = table.Column<bool>(type: "bit", nullable: false),
                    MinimumMemberLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationDuties_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDuties_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDuties_OrganizationMemberLevels_MinimumMemberLevelId",
                        column: x => x.MinimumMemberLevelId,
                        principalTable: "OrganizationMemberLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InvestigationDuties_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvestigationDutyAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationDutyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EligibilityOverridden = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationDutyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationDutyAssignments_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDutyAssignments_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDutyAssignments_InvestigationAttendees_InvestigationAttendeeId",
                        column: x => x.InvestigationAttendeeId,
                        principalTable: "InvestigationAttendees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDutyAssignments_InvestigationDuties_InvestigationDutyId",
                        column: x => x.InvestigationDutyId,
                        principalTable: "InvestigationDuties",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseContacts_AppUserId",
                table: "CaseContacts",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseContacts_CaseId_AppUserId",
                table: "CaseContacts",
                columns: new[] { "CaseId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseContacts_CreatedByAppUserId",
                table: "CaseContacts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseContacts_UpdatedByAppUserId",
                table: "CaseContacts",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDuties_CreatedByAppUserId",
                table: "InvestigationDuties",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDuties_MinimumMemberLevelId",
                table: "InvestigationDuties",
                column: "MinimumMemberLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDuties_OrganizationId_SortOrder",
                table: "InvestigationDuties",
                columns: new[] { "OrganizationId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDuties_UpdatedByAppUserId",
                table: "InvestigationDuties",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyAssignments_CreatedByAppUserId",
                table: "InvestigationDutyAssignments",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyAssignments_InvestigationAttendeeId_InvestigationDutyId",
                table: "InvestigationDutyAssignments",
                columns: new[] { "InvestigationAttendeeId", "InvestigationDutyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyAssignments_InvestigationDutyId",
                table: "InvestigationDutyAssignments",
                column: "InvestigationDutyId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyAssignments_UpdatedByAppUserId",
                table: "InvestigationDutyAssignments",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseContacts");

            migrationBuilder.DropTable(
                name: "InvestigationDutyAssignments");

            migrationBuilder.DropTable(
                name: "InvestigationDuties");
        }
    }
}
