using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreCartsAndOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GuestTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastActivityUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCarts", x => x.Id);
                    table.CheckConstraint("CK_StoreCarts_Identity", "[AppUserId] IS NOT NULL OR [GuestTokenHash] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StoreCarts_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreCarts_StoreCoupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "StoreCoupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StoreCartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCartItems", x => x.Id);
                    table.CheckConstraint("CK_StoreCartItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 100");
                    table.ForeignKey(
                        name: "FK_StoreCartItems_StoreCarts_CartId",
                        column: x => x.CartId,
                        principalTable: "StoreCarts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreCartItems_StoreProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "StoreProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StoreCartId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuyerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuyerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BuyerEmailNormalized = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShipName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShipPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ShipStreet1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShipStreet2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ShipCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShipState = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ShipZip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShipCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BillingSameAsShipping = table.Column<bool>(type: "bit", nullable: false),
                    BillName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BillCompany = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BillStreet1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillStreet2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillState = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    BillZip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BillCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingTaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CartFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlacedFromIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StripeChargeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StripeTaxCalculationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StripeTaxTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReservationExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReservationReleasedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlacedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PackedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShippedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Carrier = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrackingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NeedsAttention = table.Column<bool>(type: "bit", nullable: false),
                    AttentionReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TaxCommitAttempts = table.Column<int>(type: "int", nullable: false),
                    PendingAnonymisationSinceUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOrders", x => x.Id);
                    table.CheckConstraint("CK_StoreOrders_Total", "[Total] >= 0");
                    table.ForeignKey(
                        name: "FK_StoreOrders_AppUsers_BuyerAppUserId",
                        column: x => x.BuyerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreOrders_StoreCarts_StoreCartId",
                        column: x => x.StoreCartId,
                        principalTable: "StoreCarts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StoreOrders_StoreCoupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "StoreCoupons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreCouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerEmailNormalized = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BuyerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RedeemedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCouponRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreCouponRedemptions_AppUsers_BuyerAppUserId",
                        column: x => x.BuyerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreCouponRedemptions_StoreCoupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "StoreCoupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreCouponRedemptions_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreOrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    ActorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOrderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreOrderEvents_AppUsers_ActorAppUserId",
                        column: x => x.ActorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreOrderEvents_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VariantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StripeTaxCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    QuantityRefunded = table.Column<int>(type: "int", nullable: false),
                    QuantityRestocked = table.Column<int>(type: "int", nullable: false),
                    ImageUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOrderItems", x => x.Id);
                    table.CheckConstraint("CK_StoreOrderItems_Quantity", "[Quantity] >= 1");
                    table.ForeignKey(
                        name: "FK_StoreOrderItems_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreOrderItems_StoreProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "StoreProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreOrderItems_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreOrderItems_UploadFiles_ImageUploadFileId",
                        column: x => x.ImageUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxReversed = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Restock = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    StripeRefundId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StripeTaxReversalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRefunds", x => x.Id);
                    table.CheckConstraint("CK_StoreRefunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_StoreRefunds_AppUsers_RequestedByAppUserId",
                        column: x => x.RequestedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreRefunds_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreRefundItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRefundItems", x => x.Id);
                    table.CheckConstraint("CK_StoreRefundItems_Quantity", "[Quantity] >= 1");
                    table.ForeignKey(
                        name: "FK_StoreRefundItems_StoreOrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "StoreOrderItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreRefundItems_StoreRefunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "StoreRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreStockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Delta = table.Column<int>(type: "int", nullable: false),
                    QuantityAfter = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreStockMovements", x => x.Id);
                    table.CheckConstraint("CK_StoreStockMovements_Delta", "[Delta] <> 0");
                    table.ForeignKey(
                        name: "FK_StoreStockMovements_AppUsers_ActorAppUserId",
                        column: x => x.ActorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreStockMovements_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreStockMovements_StoreProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "StoreProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreStockMovements_StoreRefunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "StoreRefunds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCartItems_CartId_VariantId",
                table: "StoreCartItems",
                columns: new[] { "CartId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreCartItems_VariantId",
                table: "StoreCartItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCarts_AppUserId",
                table: "StoreCarts",
                column: "AppUserId",
                unique: true,
                filter: "[AppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCarts_CouponId",
                table: "StoreCarts",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCarts_GuestTokenHash",
                table: "StoreCarts",
                column: "GuestTokenHash",
                unique: true,
                filter: "[GuestTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCarts_LastActivityUtc",
                table: "StoreCarts",
                column: "LastActivityUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCouponRedemptions_BuyerAppUserId",
                table: "StoreCouponRedemptions",
                column: "BuyerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCouponRedemptions_CouponId_BuyerAppUserId",
                table: "StoreCouponRedemptions",
                columns: new[] { "CouponId", "BuyerAppUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCouponRedemptions_CouponId_BuyerEmailNormalized",
                table: "StoreCouponRedemptions",
                columns: new[] { "CouponId", "BuyerEmailNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCouponRedemptions_OrderId",
                table: "StoreCouponRedemptions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderEvents_ActorAppUserId",
                table: "StoreOrderEvents",
                column: "ActorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderEvents_OrderId_OccurredUtc",
                table: "StoreOrderEvents",
                columns: new[] { "OrderId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderItems_ImageUploadFileId",
                table: "StoreOrderItems",
                column: "ImageUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderItems_OrderId",
                table: "StoreOrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderItems_ProductId_OrderId",
                table: "StoreOrderItems",
                columns: new[] { "ProductId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderItems_VariantId",
                table: "StoreOrderItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_AccessToken",
                table: "StoreOrders",
                column: "AccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_BuyerAppUserId_PlacedUtc",
                table: "StoreOrders",
                columns: new[] { "BuyerAppUserId", "PlacedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_BuyerEmail",
                table: "StoreOrders",
                column: "BuyerEmail");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_BuyerEmailNormalized",
                table: "StoreOrders",
                column: "BuyerEmailNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_CouponId",
                table: "StoreOrders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_NeedsAttention",
                table: "StoreOrders",
                column: "NeedsAttention",
                filter: "[NeedsAttention] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_OrderNumber",
                table: "StoreOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_Status_PlacedFromIp",
                table: "StoreOrders",
                columns: new[] { "Status", "PlacedFromIp" },
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_Status_PlacedUtc",
                table: "StoreOrders",
                columns: new[] { "Status", "PlacedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_StoreCartId_Status",
                table: "StoreOrders",
                columns: new[] { "StoreCartId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrders_StripePaymentIntentId",
                table: "StoreOrders",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "[StripePaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefundItems_OrderItemId",
                table: "StoreRefundItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefundItems_RefundId_OrderItemId",
                table: "StoreRefundItems",
                columns: new[] { "RefundId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefunds_OrderId",
                table: "StoreRefunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefunds_RequestedByAppUserId",
                table: "StoreRefunds",
                column: "RequestedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefunds_StripeRefundId",
                table: "StoreRefunds",
                column: "StripeRefundId",
                unique: true,
                filter: "[StripeRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StoreStockMovements_ActorAppUserId",
                table: "StoreStockMovements",
                column: "ActorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreStockMovements_OrderId",
                table: "StoreStockMovements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreStockMovements_RefundId",
                table: "StoreStockMovements",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreStockMovements_VariantId_OccurredUtc",
                table: "StoreStockMovements",
                columns: new[] { "VariantId", "OccurredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreCartItems");

            migrationBuilder.DropTable(
                name: "StoreCouponRedemptions");

            migrationBuilder.DropTable(
                name: "StoreOrderEvents");

            migrationBuilder.DropTable(
                name: "StoreRefundItems");

            migrationBuilder.DropTable(
                name: "StoreStockMovements");

            migrationBuilder.DropTable(
                name: "StoreOrderItems");

            migrationBuilder.DropTable(
                name: "StoreRefunds");

            migrationBuilder.DropTable(
                name: "StoreOrders");

            migrationBuilder.DropTable(
                name: "StoreCarts");
        }
    }
}
