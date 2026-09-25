using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class PlaceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewState = table.Column<int>(type: "int", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ScreenerScore = table.Column<double>(type: "float", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceEvidence_AppUsers_AddedByAppUserId",
                        column: x => x.AddedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceEvidence_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceEvidence_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_AddedByAppUserId",
                table: "PlaceEvidence",
                column: "AddedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_PlaceId_ReviewState_DateCreated",
                table: "PlaceEvidence",
                columns: new[] { "PlaceId", "ReviewState", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_PlaceId_UploadFileId",
                table: "PlaceEvidence",
                columns: new[] { "PlaceId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_ReviewState_DateCreated",
                table: "PlaceEvidence",
                columns: new[] { "ReviewState", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceEvidence_UploadFileId",
                table: "PlaceEvidence",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceEvidence");
        }
    }
}
