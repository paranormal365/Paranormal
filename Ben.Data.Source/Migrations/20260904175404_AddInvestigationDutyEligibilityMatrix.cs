using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationDutyEligibilityMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Capabilities",
                table: "InvestigationDuties",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "InvestigationDutyEligibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationDutyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMemberLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationDutyEligibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationDutyEligibilities_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDutyEligibilities_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationDutyEligibilities_InvestigationDuties_InvestigationDutyId",
                        column: x => x.InvestigationDutyId,
                        principalTable: "InvestigationDuties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestigationDutyEligibilities_OrganizationMemberLevels_OrganizationMemberLevelId",
                        column: x => x.OrganizationMemberLevelId,
                        principalTable: "OrganizationMemberLevels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyEligibilities_CreatedByAppUserId",
                table: "InvestigationDutyEligibilities",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyEligibilities_InvestigationDutyId_OrganizationMemberLevelId",
                table: "InvestigationDutyEligibilities",
                columns: new[] { "InvestigationDutyId", "OrganizationMemberLevelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyEligibilities_OrganizationMemberLevelId",
                table: "InvestigationDutyEligibilities",
                column: "OrganizationMemberLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationDutyEligibilities_UpdatedByAppUserId",
                table: "InvestigationDutyEligibilities",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationDutyEligibilities");

            migrationBuilder.DropColumn(
                name: "Capabilities",
                table: "InvestigationDuties");
        }
    }
}
