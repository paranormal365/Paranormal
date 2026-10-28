using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FileProvenances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileProvenances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalysisCopyUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScopeOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceClipPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SealSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CopySha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChainJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordingOffsetMs = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Enhanced = table.Column<bool>(type: "bit", nullable: false),
                    MadeByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileProvenances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileProvenances_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileProvenances_FieldSessionUploadId",
                table: "FileProvenances",
                column: "FieldSessionUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_FileProvenances_UploadFileId",
                table: "FileProvenances",
                column: "UploadFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileProvenances");
        }
    }
}
