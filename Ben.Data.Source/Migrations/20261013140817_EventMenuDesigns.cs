using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventMenuDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventMenus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Intro = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Meal = table.Column<int>(type: "int", nullable: true),
                    ThroughoutTheEvent = table.Column<bool>(type: "bit", nullable: false),
                    DesignKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LogoUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PriceNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CopiedFromEventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConvertedFromHostedEventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMenus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventMenus_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventMenus_UploadFiles_LogoUploadFileId",
                        column: x => x.LogoUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MenuCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsStarter = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventMenuPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMenuPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventMenuPrices_EventMenus_EventMenuId",
                        column: x => x.EventMenuId,
                        principalTable: "EventMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventMenuServings",
                columns: table => new
                {
                    EventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMenuServings", x => new { x.EventMenuId, x.HostedEventMenuId });
                    table.ForeignKey(
                        name: "FK_EventMenuServings_EventMenus_EventMenuId",
                        column: x => x.EventMenuId,
                        principalTable: "EventMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventMenuServings_HostedEventMenus_HostedEventMenuId",
                        column: x => x.HostedEventMenuId,
                        principalTable: "HostedEventMenus",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventMenuSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMenuSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventMenuSections_EventMenus_EventMenuId",
                        column: x => x.EventMenuId,
                        principalTable: "EventMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventMenuSections_MenuCategories_MenuCategoryId",
                        column: x => x.MenuCategoryId,
                        principalTable: "MenuCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MenuLibraryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MenuCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImageUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Dietary = table.Column<int>(type: "int", nullable: false),
                    Allergens = table.Column<int>(type: "int", nullable: false),
                    DietaryNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RetiredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuLibraryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuLibraryItems_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuLibraryItems_MenuCategories_MenuCategoryId",
                        column: x => x.MenuCategoryId,
                        principalTable: "MenuCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuLibraryItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuLibraryItems_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuLibraryItems_UploadFiles_ImageUploadFileId",
                        column: x => x.ImageUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventMenuEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventMenuSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuLibraryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pricing = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMenuEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventMenuEntries_EventMenuSections_EventMenuSectionId",
                        column: x => x.EventMenuSectionId,
                        principalTable: "EventMenuSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventMenuEntries_MenuLibraryItems_MenuLibraryItemId",
                        column: x => x.MenuLibraryItemId,
                        principalTable: "MenuLibraryItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuEntries_EventMenuSectionId",
                table: "EventMenuEntries",
                column: "EventMenuSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuEntries_MenuLibraryItemId",
                table: "EventMenuEntries",
                column: "MenuLibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuPrices_EventMenuId",
                table: "EventMenuPrices",
                column: "EventMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenus_ConvertedFromHostedEventMenuId",
                table: "EventMenus",
                column: "ConvertedFromHostedEventMenuId",
                unique: true,
                filter: "[ConvertedFromHostedEventMenuId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenus_HostedEventId",
                table: "EventMenus",
                column: "HostedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenus_LogoUploadFileId",
                table: "EventMenus",
                column: "LogoUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuSections_EventMenuId",
                table: "EventMenuSections",
                column: "EventMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuSections_MenuCategoryId",
                table: "EventMenuSections",
                column: "MenuCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMenuServings_HostedEventMenuId",
                table: "EventMenuServings",
                column: "HostedEventMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_NameKey",
                table: "MenuCategories",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuLibraryItems_HostedEventId",
                table: "MenuLibraryItems",
                column: "HostedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuLibraryItems_ImageUploadFileId",
                table: "MenuLibraryItems",
                column: "ImageUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuLibraryItems_MenuCategoryId",
                table: "MenuLibraryItems",
                column: "MenuCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuLibraryItems_OrganizationId_Scope",
                table: "MenuLibraryItems",
                columns: new[] { "OrganizationId", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuLibraryItems_PlaceId",
                table: "MenuLibraryItems",
                column: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventMenuEntries");

            migrationBuilder.DropTable(
                name: "EventMenuPrices");

            migrationBuilder.DropTable(
                name: "EventMenuServings");

            migrationBuilder.DropTable(
                name: "EventMenuSections");

            migrationBuilder.DropTable(
                name: "MenuLibraryItems");

            migrationBuilder.DropTable(
                name: "EventMenus");

            migrationBuilder.DropTable(
                name: "MenuCategories");
        }
    }
}
