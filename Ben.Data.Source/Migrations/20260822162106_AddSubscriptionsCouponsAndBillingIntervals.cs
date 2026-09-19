using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionsCouponsAndBillingIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PercentOff = table.Column<int>(type: "int", nullable: true),
                    AmountOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: false),
                    DurationPeriods = table.Column<int>(type: "int", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    RedemptionCount = table.Column<int>(type: "int", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RedeemByUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliesToInterval = table.Column<int>(type: "int", nullable: true),
                    AppliesTo = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coupons_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Coupons_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationBillingContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationBillingContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationBillingContacts_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationBillingContacts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationBillingContacts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationBillingContacts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MinMembers = table.Column<int>(type: "int", nullable: false),
                    MaxMembers = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTiers_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTiers_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CouponCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    RedemptionCount = table.Column<int>(type: "int", nullable: false),
                    IssuedTo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RestrictedToAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponCodes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CouponCodes_AppUsers_RestrictedToAppUserId",
                        column: x => x.RestrictedToAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CouponCodes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CouponCodes_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MemberCountAtPeriodStart = table.Column<int>(type: "int", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    PriceAtPeriodStart = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "bit", nullable: false),
                    LapsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstPaidPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProviderSubscriptionRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionTierPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTierPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPrices_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPrices_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionTierPrices_SubscriptionTiers_SubscriptionTierId",
                        column: x => x.SubscriptionTierId,
                        principalTable: "SubscriptionTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CouponCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodsRemaining = table.Column<int>(type: "int", nullable: true),
                    RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_CouponCodes_CouponCodeId",
                        column: x => x.CouponCodeId,
                        principalTable: "CouponCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouponCodes_Code",
                table: "CouponCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponCodes_CouponId",
                table: "CouponCodes",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponCodes_CreatedByAppUserId",
                table: "CouponCodes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponCodes_RestrictedToAppUserId",
                table: "CouponCodes",
                column: "RestrictedToAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponCodes_UpdatedByAppUserId",
                table: "CouponCodes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponCodeId",
                table: "CouponRedemptions",
                column: "CouponCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_OrganizationId",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CreatedByAppUserId",
                table: "CouponRedemptions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_OrganizationId",
                table: "CouponRedemptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_UpdatedByAppUserId",
                table: "CouponRedemptions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_CreatedByAppUserId",
                table: "Coupons",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_UpdatedByAppUserId",
                table: "Coupons",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingContacts_AppUserId",
                table: "OrganizationBillingContacts",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingContacts_CreatedByAppUserId",
                table: "OrganizationBillingContacts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingContacts_OrganizationId_AppUserId",
                table: "OrganizationBillingContacts",
                columns: new[] { "OrganizationId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingContacts_UpdatedByAppUserId",
                table: "OrganizationBillingContacts",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_CreatedByAppUserId",
                table: "OrganizationSubscriptions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_OrganizationId",
                table: "OrganizationSubscriptions",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_Status_CurrentPeriodEnd",
                table: "OrganizationSubscriptions",
                columns: new[] { "Status", "CurrentPeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_SubscriptionTierId",
                table: "OrganizationSubscriptions",
                column: "SubscriptionTierId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_UpdatedByAppUserId",
                table: "OrganizationSubscriptions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPrices_CreatedByAppUserId",
                table: "SubscriptionTierPrices",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPrices_SubscriptionTierId_Interval",
                table: "SubscriptionTierPrices",
                columns: new[] { "SubscriptionTierId", "Interval" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierPrices_UpdatedByAppUserId",
                table: "SubscriptionTierPrices",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTiers_CreatedByAppUserId",
                table: "SubscriptionTiers",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTiers_IsActive_MinMembers",
                table: "SubscriptionTiers",
                columns: new[] { "IsActive", "MinMembers" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTiers_UpdatedByAppUserId",
                table: "SubscriptionTiers",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CouponRedemptions");

            migrationBuilder.DropTable(
                name: "OrganizationBillingContacts");

            migrationBuilder.DropTable(
                name: "OrganizationSubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionTierPrices");

            migrationBuilder.DropTable(
                name: "CouponCodes");

            migrationBuilder.DropTable(
                name: "SubscriptionTiers");

            migrationBuilder.DropTable(
                name: "Coupons");
        }
    }
}
