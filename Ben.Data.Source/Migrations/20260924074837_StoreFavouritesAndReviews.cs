using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreFavouritesAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreFavourites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreFavourites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreFavourites_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreFavourites_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HelpfulCount = table.Column<int>(type: "int", nullable: false),
                    ModeratedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModeratedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminReply = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AdminReplyByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminRepliedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreReviews", x => x.Id);
                    table.CheckConstraint("CK_StoreReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_StoreReviews_AppUsers_AdminReplyByAppUserId",
                        column: x => x.AdminReplyByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_AppUsers_ModeratedByAppUserId",
                        column: x => x.ModeratedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviews_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreReviewVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreReviewVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreReviewVotes_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreReviewVotes_StoreReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "StoreReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreFavourites_AppUserId_ProductId",
                table: "StoreFavourites",
                columns: new[] { "AppUserId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreFavourites_ProductId",
                table: "StoreFavourites",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_AdminReplyByAppUserId",
                table: "StoreReviews",
                column: "AdminReplyByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_AuthorAppUserId",
                table: "StoreReviews",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_CreatedByAppUserId",
                table: "StoreReviews",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_ModeratedByAppUserId",
                table: "StoreReviews",
                column: "ModeratedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_OrderId",
                table: "StoreReviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_ProductId_AuthorAppUserId",
                table: "StoreReviews",
                columns: new[] { "ProductId", "AuthorAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_ProductId_Status_DateCreated",
                table: "StoreReviews",
                columns: new[] { "ProductId", "Status", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_ProductId_Status_HelpfulCount",
                table: "StoreReviews",
                columns: new[] { "ProductId", "Status", "HelpfulCount" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_Status",
                table: "StoreReviews",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_UpdatedByAppUserId",
                table: "StoreReviews",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviewVotes_AppUserId",
                table: "StoreReviewVotes",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviewVotes_ReviewId_AppUserId",
                table: "StoreReviewVotes",
                columns: new[] { "ReviewId", "AppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreFavourites");

            migrationBuilder.DropTable(
                name: "StoreReviewVotes");

            migrationBuilder.DropTable(
                name: "StoreReviews");
        }
    }
}
