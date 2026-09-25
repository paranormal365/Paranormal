using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImageUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsNew = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreCategories_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreCategories_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreCategories_UploadFiles_ImageUploadFileId",
                        column: x => x.ImageUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreCoupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PercentOff = table.Column<int>(type: "int", nullable: true),
                    AmountOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MinimumOrderAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StartsUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    RedemptionCount = table.Column<int>(type: "int", nullable: false),
                    MaxRedemptionsPerBuyer = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCoupons", x => x.Id);
                    table.CheckConstraint("CK_StoreCoupons_PercentOff", "[PercentOff] IS NULL OR ([PercentOff] >= 1 AND [PercentOff] <= 100)");
                    table.ForeignKey(
                        name: "FK_StoreCoupons_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreCoupons_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LongDescriptionHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    NewUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StripeTaxCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MinPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitsSold = table.Column<int>(type: "int", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    AverageRating = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProducts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProducts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProducts_EquipmentModels_EquipmentModelId",
                        column: x => x.EquipmentModelId,
                        principalTable: "EquipmentModels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProducts_StoreCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "StoreCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreProductOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductOptions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductOptions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductOptions_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProductSpecs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductSpecs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductSpecs_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductSpecs_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductSpecs_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OptionSignature = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StockOnHand = table.Column<int>(type: "int", nullable: false),
                    StockReserved = table.Column<int>(type: "int", nullable: false),
                    UnitsSold = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductVariants", x => x.Id);
                    table.CheckConstraint("CK_StoreProductVariants_CompareAtPrice", "[CompareAtPrice] IS NULL OR [CompareAtPrice] > [Price]");
                    table.CheckConstraint("CK_StoreProductVariants_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_StoreProductVariants_Stock", "[StockOnHand] >= 0 AND [StockReserved] >= 0 AND [StockReserved] <= [StockOnHand]");
                    table.ForeignKey(
                        name: "FK_StoreProductVariants_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductVariants_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductVariants_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProductOptionValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SwatchHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductOptionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductOptionValues_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductOptionValues_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductOptionValues_StoreProductOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "StoreProductOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductImages_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductImages_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductImages_StoreProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "StoreProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductImages_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreProductImages_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreProductVariantOptionValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductVariantOptionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductVariantOptionValues_StoreProductOptionValues_OptionValueId",
                        column: x => x.OptionValueId,
                        principalTable: "StoreProductOptionValues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductVariantOptionValues_StoreProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "StoreProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_CreatedByAppUserId",
                table: "StoreCategories",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_ImageUploadFileId",
                table: "StoreCategories",
                column: "ImageUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_IsActive_SortOrder",
                table: "StoreCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_Slug",
                table: "StoreCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_UpdatedByAppUserId",
                table: "StoreCategories",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCoupons_Code",
                table: "StoreCoupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreCoupons_CreatedByAppUserId",
                table: "StoreCoupons",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCoupons_UpdatedByAppUserId",
                table: "StoreCoupons",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductImages_CreatedByAppUserId",
                table: "StoreProductImages",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductImages_ProductId_UploadFileId",
                table: "StoreProductImages",
                columns: new[] { "ProductId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductImages_UpdatedByAppUserId",
                table: "StoreProductImages",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductImages_UploadFileId",
                table: "StoreProductImages",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductImages_VariantId",
                table: "StoreProductImages",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptions_CreatedByAppUserId",
                table: "StoreProductOptions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptions_ProductId_Name",
                table: "StoreProductOptions",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptions_UpdatedByAppUserId",
                table: "StoreProductOptions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptionValues_CreatedByAppUserId",
                table: "StoreProductOptionValues",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptionValues_OptionId_Value",
                table: "StoreProductOptionValues",
                columns: new[] { "OptionId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductOptionValues_UpdatedByAppUserId",
                table: "StoreProductOptionValues",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_CategoryId_IsActive_SortOrder",
                table: "StoreProducts",
                columns: new[] { "CategoryId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_CreatedByAppUserId",
                table: "StoreProducts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_EquipmentModelId",
                table: "StoreProducts",
                column: "EquipmentModelId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_IsActive_DateCreated",
                table: "StoreProducts",
                columns: new[] { "IsActive", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_IsActive_MinPrice",
                table: "StoreProducts",
                columns: new[] { "IsActive", "MinPrice" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_IsActive_UnitsSold",
                table: "StoreProducts",
                columns: new[] { "IsActive", "UnitsSold" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_Slug",
                table: "StoreProducts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_UpdatedByAppUserId",
                table: "StoreProducts",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSpecs_CreatedByAppUserId",
                table: "StoreProductSpecs",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSpecs_ProductId",
                table: "StoreProductSpecs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSpecs_UpdatedByAppUserId",
                table: "StoreProductSpecs",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariantOptionValues_OptionValueId",
                table: "StoreProductVariantOptionValues",
                column: "OptionValueId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariantOptionValues_VariantId_OptionValueId",
                table: "StoreProductVariantOptionValues",
                columns: new[] { "VariantId", "OptionValueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariants_CreatedByAppUserId",
                table: "StoreProductVariants",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariants_ProductId_OptionSignature",
                table: "StoreProductVariants",
                columns: new[] { "ProductId", "OptionSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariants_Sku",
                table: "StoreProductVariants",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVariants_UpdatedByAppUserId",
                table: "StoreProductVariants",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreCoupons");

            migrationBuilder.DropTable(
                name: "StoreProductImages");

            migrationBuilder.DropTable(
                name: "StoreProductSpecs");

            migrationBuilder.DropTable(
                name: "StoreProductVariantOptionValues");

            migrationBuilder.DropTable(
                name: "StoreProductOptionValues");

            migrationBuilder.DropTable(
                name: "StoreProductVariants");

            migrationBuilder.DropTable(
                name: "StoreProductOptions");

            migrationBuilder.DropTable(
                name: "StoreProducts");

            migrationBuilder.DropTable(
                name: "StoreCategories");
        }
    }
}
