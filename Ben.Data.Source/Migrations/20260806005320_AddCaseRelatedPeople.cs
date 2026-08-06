using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseRelatedPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseRelatedPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Age = table.Column<int>(type: "int", nullable: true),
                    Relationship = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LivesAtProperty = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseRelatedPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseRelatedPeople_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseRelatedPeople_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseRelatedPeople_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseRelatedPeople_CaseId",
                table: "CaseRelatedPeople",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseRelatedPeople_CreatedByAppUserId",
                table: "CaseRelatedPeople",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseRelatedPeople_UpdatedByAppUserId",
                table: "CaseRelatedPeople",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseRelatedPeople");
        }
    }
}
