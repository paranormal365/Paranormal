using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddExperienceTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExperienceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ProposedByOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceCategories_AppUsers_ApprovedByAppUserId",
                        column: x => x.ApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceCategories_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceCategories_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceCategories_Organizations_ProposedByOrganizationId",
                        column: x => x.ProposedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExperienceTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ProposedByOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceTypes_AppUsers_ApprovedByAppUserId",
                        column: x => x.ApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExperienceTypes_ExperienceCategories_ExperienceCategoryId",
                        column: x => x.ExperienceCategoryId,
                        principalTable: "ExperienceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExperienceTypes_Organizations_ProposedByOrganizationId",
                        column: x => x.ProposedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceCategories_ApprovedByAppUserId",
                table: "ExperienceCategories",
                column: "ApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceCategories_CreatedByAppUserId",
                table: "ExperienceCategories",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceCategories_ProposedByOrganizationId",
                table: "ExperienceCategories",
                column: "ProposedByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceCategories_UpdatedByAppUserId",
                table: "ExperienceCategories",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_ApprovedByAppUserId",
                table: "ExperienceTypes",
                column: "ApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_CreatedByAppUserId",
                table: "ExperienceTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_ExperienceCategoryId",
                table: "ExperienceTypes",
                column: "ExperienceCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_ProposedByOrganizationId",
                table: "ExperienceTypes",
                column: "ProposedByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_UpdatedByAppUserId",
                table: "ExperienceTypes",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExperienceTypes");

            migrationBuilder.DropTable(
                name: "ExperienceCategories");
        }
    }
}
