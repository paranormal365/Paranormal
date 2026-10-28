using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class PlaceScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceScanCustodies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceScanCustodies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceScanCustodies_AppUsers_GrantedByAppUserId",
                        column: x => x.GrantedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScanCustodies_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScanCustodies_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceScanHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    RoomName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TakenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceScanHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceScanHolds_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceScanHolds_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    RoomName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PlaceRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    Retention = table.Column<byte>(type: "tinyint", nullable: false),
                    ScannedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScannedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScannedForOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ShownOnPlacePage = table.Column<bool>(type: "bit", nullable: false),
                    DeviceModel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ScannerVersion = table.Column<int>(type: "int", nullable: false),
                    FloorAreaM2 = table.Column<double>(type: "float", nullable: true),
                    TriangleCount = table.Column<int>(type: "int", nullable: false),
                    KeyframeCount = table.Column<int>(type: "int", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    YearNoticeSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceScans_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScans_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScans_AppUsers_ScannedByAppUserId",
                        column: x => x.ScannedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScans_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceScans_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceScanChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ObjectCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DistanceCm = table.Column<double>(type: "float", nullable: true),
                    RotationDeg = table.Column<double>(type: "float", nullable: true),
                    SizeXCm = table.Column<double>(type: "float", nullable: true),
                    SizeYCm = table.Column<double>(type: "float", nullable: true),
                    SizeZCm = table.Column<double>(type: "float", nullable: true),
                    WhereX = table.Column<double>(type: "float", nullable: false),
                    WhereY = table.Column<double>(type: "float", nullable: false),
                    WhereZ = table.Column<double>(type: "float", nullable: false),
                    PictureEntryPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ComparedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceScanChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceScanChanges_FieldSessionUploads_FieldSessionUploadId",
                        column: x => x.FieldSessionUploadId,
                        principalTable: "FieldSessionUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceScanChanges_PlaceScans_PlaceScanId",
                        column: x => x.PlaceScanId,
                        principalTable: "PlaceScans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanChanges_FieldSessionUploadId",
                table: "PlaceScanChanges",
                column: "FieldSessionUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanChanges_PlaceScanId_ComparedUtc",
                table: "PlaceScanChanges",
                columns: new[] { "PlaceScanId", "ComparedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanCustodies_GrantedByAppUserId",
                table: "PlaceScanCustodies",
                column: "GrantedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanCustodies_OrganizationId",
                table: "PlaceScanCustodies",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanCustodies_PlaceId",
                table: "PlaceScanCustodies",
                column: "PlaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanHolds_AppUserId",
                table: "PlaceScanHolds",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScanHolds_PlaceId_RoomKey_ExpiresUtc",
                table: "PlaceScanHolds",
                columns: new[] { "PlaceId", "RoomKey", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_CreatedByAppUserId",
                table: "PlaceScans",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_DecidedByAppUserId",
                table: "PlaceScans",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_PlaceId_RoomKey_Current",
                table: "PlaceScans",
                columns: new[] { "PlaceId", "RoomKey" },
                unique: true,
                filter: "[State] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_PlaceId_RoomKey_Version",
                table: "PlaceScans",
                columns: new[] { "PlaceId", "RoomKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_ScannedByAppUserId",
                table: "PlaceScans",
                column: "ScannedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceScans_UpdatedByAppUserId",
                table: "PlaceScans",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceScanChanges");

            migrationBuilder.DropTable(
                name: "PlaceScanCustodies");

            migrationBuilder.DropTable(
                name: "PlaceScanHolds");

            migrationBuilder.DropTable(
                name: "PlaceScans");
        }
    }
}
