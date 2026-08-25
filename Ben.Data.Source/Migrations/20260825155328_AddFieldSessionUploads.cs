using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSessionUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldSessionUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeviceSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LocationLabel = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadingCount = table.Column<int>(type: "int", nullable: false),
                    MarkerCount = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSessionUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_AppUsers_RecordedByAppUserId",
                        column: x => x.RecordedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_AppUsers_SubmittedByAppUserId",
                        column: x => x.SubmittedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FieldSessionUploads_UploadFiles_DocumentUploadFileId",
                        column: x => x.DocumentUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FieldSessionUploadFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DigestMatched = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSessionUploadFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldSessionUploadFiles_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploadFiles_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionUploadFiles_FieldSessionUploads_FieldSessionUploadId",
                        column: x => x.FieldSessionUploadId,
                        principalTable: "FieldSessionUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FieldSessionUploadFiles_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploadFiles_CreatedByAppUserId",
                table: "FieldSessionUploadFiles",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploadFiles_FieldSessionUploadId_RelativePath",
                table: "FieldSessionUploadFiles",
                columns: new[] { "FieldSessionUploadId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploadFiles_UpdatedByAppUserId",
                table: "FieldSessionUploadFiles",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploadFiles_UploadFileId",
                table: "FieldSessionUploadFiles",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_CreatedByAppUserId",
                table: "FieldSessionUploads",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_DocumentUploadFileId",
                table: "FieldSessionUploads",
                column: "DocumentUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_InvestigationId_StartedAt",
                table: "FieldSessionUploads",
                columns: new[] { "InvestigationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_RecordedByAppUserId",
                table: "FieldSessionUploads",
                column: "RecordedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_SubmittedByAppUserId_DeviceSessionId",
                table: "FieldSessionUploads",
                columns: new[] { "SubmittedByAppUserId", "DeviceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_SubmittedByAppUserId_StartedAt",
                table: "FieldSessionUploads",
                columns: new[] { "SubmittedByAppUserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_UpdatedByAppUserId",
                table: "FieldSessionUploads",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldSessionUploadFiles");

            migrationBuilder.DropTable(
                name: "FieldSessionUploads");
        }
    }
}
