using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DraftSubject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DraftBodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftSavedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DraftAuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Kind",
                table: "EmailTemplates",
                column: "Kind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailTemplates");
        }
    }
}
