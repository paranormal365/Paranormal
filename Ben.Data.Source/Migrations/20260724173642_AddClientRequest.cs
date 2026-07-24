using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StreetAddress1 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StreetAddress2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    BirthYear = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRequests_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequests_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequests_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientRequestFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRequestFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRequestFiles_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestFiles_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestFiles_ClientRequests_ClientRequestId",
                        column: x => x.ClientRequestId,
                        principalTable: "ClientRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientRequestFiles_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientRequestOrganizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DateApplied = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateResponded = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRequestOrganizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRequestOrganizations_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestOrganizations_AppUsers_RespondedByAppUserId",
                        column: x => x.RespondedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestOrganizations_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestOrganizations_ClientRequests_ClientRequestId",
                        column: x => x.ClientRequestId,
                        principalTable: "ClientRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientRequestOrganizations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestFiles_ClientRequestId_UploadFileId",
                table: "ClientRequestFiles",
                columns: new[] { "ClientRequestId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestFiles_CreatedByAppUserId",
                table: "ClientRequestFiles",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestFiles_UpdatedByAppUserId",
                table: "ClientRequestFiles",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestFiles_UploadFileId",
                table: "ClientRequestFiles",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestOrganizations_ClientRequestId_OrganizationId",
                table: "ClientRequestOrganizations",
                columns: new[] { "ClientRequestId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestOrganizations_CreatedByAppUserId",
                table: "ClientRequestOrganizations",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestOrganizations_OrganizationId",
                table: "ClientRequestOrganizations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestOrganizations_RespondedByAppUserId",
                table: "ClientRequestOrganizations",
                column: "RespondedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestOrganizations_UpdatedByAppUserId",
                table: "ClientRequestOrganizations",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequests_AppUserId",
                table: "ClientRequests",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequests_CreatedByAppUserId",
                table: "ClientRequests",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequests_UpdatedByAppUserId",
                table: "ClientRequests",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientRequestFiles");

            migrationBuilder.DropTable(
                name: "ClientRequestOrganizations");

            migrationBuilder.DropTable(
                name: "ClientRequests");
        }
    }
}
