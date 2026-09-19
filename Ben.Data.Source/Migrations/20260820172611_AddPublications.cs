using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UrlName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publications_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Publications_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Publications_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PublicationPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UrlName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Excerpt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequiredTier = table.Column<int>(type: "int", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationPosts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PublicationPosts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PublicationPosts_Publications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "Publications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublicationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriberAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationSubscriptions_AppUsers_SubscriberAppUserId",
                        column: x => x.SubscriberAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PublicationSubscriptions_Publications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "Publications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPosts_CreatedByAppUserId",
                table: "PublicationPosts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPosts_PublicationId_PublishedUtc",
                table: "PublicationPosts",
                columns: new[] { "PublicationId", "PublishedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPosts_PublicationId_UrlName",
                table: "PublicationPosts",
                columns: new[] { "PublicationId", "UrlName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPosts_UpdatedByAppUserId",
                table: "PublicationPosts",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_CreatedByAppUserId",
                table: "Publications",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_OrganizationId",
                table: "Publications",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_UpdatedByAppUserId",
                table: "Publications",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_UrlName",
                table: "Publications",
                column: "UrlName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationSubscriptions_PublicationId_SubscriberAppUserId",
                table: "PublicationSubscriptions",
                columns: new[] { "PublicationId", "SubscriberAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationSubscriptions_SubscriberAppUserId_CancelledUtc",
                table: "PublicationSubscriptions",
                columns: new[] { "SubscriberAppUserId", "CancelledUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationPosts");

            migrationBuilder.DropTable(
                name: "PublicationSubscriptions");

            migrationBuilder.DropTable(
                name: "Publications");
        }
    }
}
