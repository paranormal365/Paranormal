using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class MailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    To = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AcceptedBySmtpUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BodyScrubbedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEmailAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboxEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ByteCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmailAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxEmailAttachments_OutboxEmails_OutboxEmailId",
                        column: x => x.OutboxEmailId,
                        principalTable: "OutboxEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmailAttachments_OutboxEmailId",
                table: "OutboxEmailAttachments",
                column: "OutboxEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_CreatedUtc",
                table: "OutboxEmails",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_Kind_CreatedUtc",
                table: "OutboxEmails",
                columns: new[] { "Kind", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_NextAttemptUtc",
                table: "OutboxEmails",
                column: "NextAttemptUtc",
                filter: "[AcceptedBySmtpUtc] IS NULL AND [FailedUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxEmailAttachments");

            migrationBuilder.DropTable(
                name: "OutboxEmails");
        }
    }
}
