using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRequestReviewVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientRequestReviewVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoterAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InFavor = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DateVoted = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRequestReviewVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRequestReviewVotes_AppUsers_VoterAppUserId",
                        column: x => x.VoterAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientRequestReviewVotes_ClientRequestOrganizations_ClientRequestOrganizationId",
                        column: x => x.ClientRequestOrganizationId,
                        principalTable: "ClientRequestOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ClientRequestOrganizations_OneAcceptedPerRequest",
                table: "ClientRequestOrganizations",
                column: "ClientRequestId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestReviewVotes_ClientRequestOrganizationId_VoterAppUserId",
                table: "ClientRequestReviewVotes",
                columns: new[] { "ClientRequestOrganizationId", "VoterAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequestReviewVotes_VoterAppUserId",
                table: "ClientRequestReviewVotes",
                column: "VoterAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientRequestReviewVotes");

            migrationBuilder.DropIndex(
                name: "UX_ClientRequestOrganizations_OneAcceptedPerRequest",
                table: "ClientRequestOrganizations");
        }
    }
}
