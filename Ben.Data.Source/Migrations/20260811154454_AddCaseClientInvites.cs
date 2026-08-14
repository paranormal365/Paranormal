using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseClientInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseClientInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DateExpires = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateAccepted = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateRevoked = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseClientInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseClientInvites_AppUsers_AcceptedByAppUserId",
                        column: x => x.AcceptedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientInvites_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientInvites_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientInvites_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientInvites_AcceptedByAppUserId",
                table: "CaseClientInvites",
                column: "AcceptedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientInvites_CaseId_Email",
                table: "CaseClientInvites",
                columns: new[] { "CaseId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientInvites_CreatedByAppUserId",
                table: "CaseClientInvites",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientInvites_Token",
                table: "CaseClientInvites",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientInvites_UpdatedByAppUserId",
                table: "CaseClientInvites",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseClientInvites");
        }
    }
}
