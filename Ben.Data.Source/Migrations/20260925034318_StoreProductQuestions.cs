using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FaqEnabled",
                table: "StoreProducts",
                type: "bit",
                nullable: false,
                // Every product already here starts with its FAQ on, as a new one does (the entity's default).
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "StoreProductFaqs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductFaqs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductFaqs_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProductQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AskerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnsweredByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnsweredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PromotedFaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductQuestions_AppUsers_AskerAppUserId",
                        column: x => x.AskerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductQuestions_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductFaqs_ProductId_SortOrder",
                table: "StoreProductFaqs",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductQuestions_AskerAppUserId_DateCreated",
                table: "StoreProductQuestions",
                columns: new[] { "AskerAppUserId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductQuestions_ProductId_Status",
                table: "StoreProductQuestions",
                columns: new[] { "ProductId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreProductFaqs");

            migrationBuilder.DropTable(
                name: "StoreProductQuestions");

            migrationBuilder.DropColumn(
                name: "FaqEnabled",
                table: "StoreProducts");
        }
    }
}
