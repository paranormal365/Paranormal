using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AccessToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromEmail = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Topic = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceIpHash = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateClosed = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTickets_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportTickets_AppUsers_AssignedToAppUserId",
                        column: x => x.AssignedToAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketReplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupportTicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFromStaff = table.Column<bool>(type: "bit", nullable: false),
                    IsInternalNote = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketReplies_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportTicketReplies_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketReplies_AuthorAppUserId",
                table: "SupportTicketReplies",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketReplies_SupportTicketId",
                table: "SupportTicketReplies",
                column: "SupportTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AccessToken",
                table: "SupportTickets",
                column: "AccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AppUserId",
                table: "SupportTickets",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedToAppUserId",
                table: "SupportTickets",
                column: "AssignedToAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_FromEmail_DateCreated",
                table: "SupportTickets",
                columns: new[] { "FromEmail", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Reference",
                table: "SupportTickets",
                column: "Reference",
                unique: true,
                filter: "[Reference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SourceIpHash_DateCreated",
                table: "SupportTickets",
                columns: new[] { "SourceIpHash", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_DateCreated",
                table: "SupportTickets",
                columns: new[] { "Status", "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportTicketReplies");

            migrationBuilder.DropTable(
                name: "SupportTickets");
        }
    }
}
