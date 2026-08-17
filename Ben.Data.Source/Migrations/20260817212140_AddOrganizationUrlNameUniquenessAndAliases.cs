using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationUrlNameUniquenessAndAliases : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// The unique index cannot be created while two organizations share an address, and sharing
        /// one was possible right up until this migration: the rename path never checked, and there
        /// was no index behind it. The fold below runs first, and keeps the <b>oldest</b> holder on
        /// the bare name — it is the one whose links have been in circulation longest.
        ///
        /// <para>Written defensively rather than after inspecting one machine's data, because a
        /// migration runs against databases this build has never seen.</para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Trim and lowercase everything first, so the duplicate check below compares what the
            // application will actually compare. Both write paths did this; one of them arrived
            // late, so older rows may predate it.
            migrationBuilder.Sql(
                "UPDATE Organizations SET UrlName = LOWER(LTRIM(RTRIM(UrlName))) WHERE UrlName IS NOT NULL;");

            // Suffix the later holders of a shared address. Renaming somebody's public address is
            // not a thing to do lightly, but two organizations answering to it is already broken —
            // the page served was whichever row the database happened to return.
            migrationBuilder.Sql("""
                WITH Duplicates AS (
                    SELECT Id, UrlName,
                           ROW_NUMBER() OVER (PARTITION BY UrlName ORDER BY DateCreated, Id) AS Rn
                    FROM Organizations
                    WHERE UrlName IS NOT NULL
                )
                UPDATE d
                SET d.UrlName = LEFT(dup.UrlName, 96) + '-' + CAST(dup.Rn AS nvarchar(3))
                FROM Organizations d
                INNER JOIN Duplicates dup ON dup.Id = d.Id
                WHERE dup.Rn > 1;
                """);

            migrationBuilder.CreateTable(
                name: "OrganizationUrlNameAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UrlName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUrlNameAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationUrlNameAliases_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_UrlName",
                table: "Organizations",
                column: "UrlName",
                unique: true,
                filter: "[UrlName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUrlNameAliases_OrganizationId",
                table: "OrganizationUrlNameAliases",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUrlNameAliases_UrlName",
                table: "OrganizationUrlNameAliases",
                column: "UrlName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationUrlNameAliases");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_UrlName",
                table: "Organizations");
        }
    }
}
