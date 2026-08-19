using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentFaqAndQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentItemFaqs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentItemFaqs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentItemFaqs_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItemFaqs_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItemFaqs_EquipmentItems_EquipmentItemId",
                        column: x => x.EquipmentItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AskedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AnsweredByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnsweredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PromotedToFaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentQuestions_AppUsers_AnsweredByAppUserId",
                        column: x => x.AnsweredByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentQuestions_AppUsers_AskedByAppUserId",
                        column: x => x.AskedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentQuestions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentQuestions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentQuestions_EquipmentItems_EquipmentItemId",
                        column: x => x.EquipmentItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemFaqs_CreatedByAppUserId",
                table: "EquipmentItemFaqs",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemFaqs_EquipmentItemId_SortOrder",
                table: "EquipmentItemFaqs",
                columns: new[] { "EquipmentItemId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemFaqs_UpdatedByAppUserId",
                table: "EquipmentItemFaqs",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentQuestions_AnsweredByAppUserId",
                table: "EquipmentQuestions",
                column: "AnsweredByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentQuestions_AskedByAppUserId",
                table: "EquipmentQuestions",
                column: "AskedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentQuestions_CreatedByAppUserId",
                table: "EquipmentQuestions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentQuestions_EquipmentItemId_Status",
                table: "EquipmentQuestions",
                columns: new[] { "EquipmentItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentQuestions_UpdatedByAppUserId",
                table: "EquipmentQuestions",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentItemFaqs");

            migrationBuilder.DropTable(
                name: "EquipmentQuestions");
        }
    }
}
