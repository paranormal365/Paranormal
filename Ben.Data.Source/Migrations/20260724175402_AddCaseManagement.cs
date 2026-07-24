using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaseId",
                table: "OrganizationPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CaseManagerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StreetAddress1 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StreetAddress2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    PublicPseudonym = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    DateCaseOpened = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateCaseClosed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cases_AppUsers_CaseManagerAppUserId",
                        column: x => x.CaseManagerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cases_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cases_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cases_ClientRequests_ClientRequestId",
                        column: x => x.ClientRequestId,
                        principalTable: "ClientRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cases_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CaseTimelineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    EventDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTimelineEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntries_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntries_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntries_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntries_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CaseTimelineEntryExperienceTypes",
                columns: table => new
                {
                    CaseTimelineEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTimelineEntryExperienceTypes", x => new { x.CaseTimelineEntryId, x.ExperienceTypeId });
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntryExperienceTypes_CaseTimelineEntries_CaseTimelineEntryId",
                        column: x => x.CaseTimelineEntryId,
                        principalTable: "CaseTimelineEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntryExperienceTypes_ExperienceTypes_ExperienceTypeId",
                        column: x => x.ExperienceTypeId,
                        principalTable: "ExperienceTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CaseTimelineEntryFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseTimelineEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTimelineEntryFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntryFiles_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntryFiles_CaseTimelineEntries_CaseTimelineEntryId",
                        column: x => x.CaseTimelineEntryId,
                        principalTable: "CaseTimelineEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaseTimelineEntryFiles_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_CaseId",
                table: "OrganizationPages",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_CaseManagerAppUserId",
                table: "Cases",
                column: "CaseManagerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_ClientRequestId",
                table: "Cases",
                column: "ClientRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_CreatedByAppUserId",
                table: "Cases",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_OrganizationId",
                table: "Cases",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_UpdatedByAppUserId",
                table: "Cases",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntries_AuthorAppUserId",
                table: "CaseTimelineEntries",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntries_CaseId",
                table: "CaseTimelineEntries",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntries_CreatedByAppUserId",
                table: "CaseTimelineEntries",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntries_UpdatedByAppUserId",
                table: "CaseTimelineEntries",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntryExperienceTypes_ExperienceTypeId",
                table: "CaseTimelineEntryExperienceTypes",
                column: "ExperienceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntryFiles_CaseTimelineEntryId_UploadFileId",
                table: "CaseTimelineEntryFiles",
                columns: new[] { "CaseTimelineEntryId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntryFiles_CreatedByAppUserId",
                table: "CaseTimelineEntryFiles",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntryFiles_UploadFileId",
                table: "CaseTimelineEntryFiles",
                column: "UploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationPages_Cases_CaseId",
                table: "OrganizationPages",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationPages_Cases_CaseId",
                table: "OrganizationPages");

            migrationBuilder.DropTable(
                name: "CaseTimelineEntryExperienceTypes");

            migrationBuilder.DropTable(
                name: "CaseTimelineEntryFiles");

            migrationBuilder.DropTable(
                name: "CaseTimelineEntries");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationPages_CaseId",
                table: "OrganizationPages");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "OrganizationPages");
        }
    }
}
