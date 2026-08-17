using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentBrands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ProposedByOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProposedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentBrands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentBrands_AppUsers_ApprovedByAppUserId",
                        column: x => x.ApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentBrands_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentBrands_AppUsers_ProposedByAppUserId",
                        column: x => x.ProposedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentBrands_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentBrands_Organizations_ProposedByOrganizationId",
                        column: x => x.ProposedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentCategories_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentCategories_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ModelNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ProposedByOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProposedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentModels_AppUsers_ApprovedByAppUserId",
                        column: x => x.ApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentModels_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentModels_AppUsers_ProposedByAppUserId",
                        column: x => x.ProposedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentModels_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentModels_EquipmentBrands_EquipmentBrandId",
                        column: x => x.EquipmentBrandId,
                        principalTable: "EquipmentBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentModels_EquipmentCategories_EquipmentCategoryId",
                        column: x => x.EquipmentCategoryId,
                        principalTable: "EquipmentCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentModels_Organizations_ProposedByOrganizationId",
                        column: x => x.ProposedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwningOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EquipmentModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false),
                    CurrentHolderAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastServicedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DefectNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentItems_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_AppUsers_CurrentHolderAppUserId",
                        column: x => x.CurrentHolderAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_AppUsers_OwnerAppUserId",
                        column: x => x.OwnerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_EquipmentModels_EquipmentModelId",
                        column: x => x.EquipmentModelId,
                        principalTable: "EquipmentModels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_Organizations_OwningOrganizationId",
                        column: x => x.OwningOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentItemPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentItemPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentItemPhotos_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItemPhotos_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentItemPhotos_EquipmentItems_EquipmentItemId",
                        column: x => x.EquipmentItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentItemPhotos_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_ApprovedByAppUserId",
                table: "EquipmentBrands",
                column: "ApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_CreatedByAppUserId",
                table: "EquipmentBrands",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_Name",
                table: "EquipmentBrands",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_ProposedByAppUserId",
                table: "EquipmentBrands",
                column: "ProposedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_ProposedByOrganizationId",
                table: "EquipmentBrands",
                column: "ProposedByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_UpdatedByAppUserId",
                table: "EquipmentBrands",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCategories_CreatedByAppUserId",
                table: "EquipmentCategories",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCategories_Name",
                table: "EquipmentCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCategories_UpdatedByAppUserId",
                table: "EquipmentCategories",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemPhotos_CreatedByAppUserId",
                table: "EquipmentItemPhotos",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemPhotos_EquipmentItemId_UploadFileId",
                table: "EquipmentItemPhotos",
                columns: new[] { "EquipmentItemId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemPhotos_UpdatedByAppUserId",
                table: "EquipmentItemPhotos",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItemPhotos_UploadFileId",
                table: "EquipmentItemPhotos",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_CreatedByAppUserId",
                table: "EquipmentItems",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_CurrentHolderAppUserId",
                table: "EquipmentItems",
                column: "CurrentHolderAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_EquipmentModelId",
                table: "EquipmentItems",
                column: "EquipmentModelId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_OwnerAppUserId",
                table: "EquipmentItems",
                column: "OwnerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_OwningOrganizationId",
                table: "EquipmentItems",
                column: "OwningOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_UpdatedByAppUserId",
                table: "EquipmentItems",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_ApprovedByAppUserId",
                table: "EquipmentModels",
                column: "ApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_CreatedByAppUserId",
                table: "EquipmentModels",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_EquipmentBrandId_Name",
                table: "EquipmentModels",
                columns: new[] { "EquipmentBrandId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_EquipmentCategoryId",
                table: "EquipmentModels",
                column: "EquipmentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_ProposedByAppUserId",
                table: "EquipmentModels",
                column: "ProposedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_ProposedByOrganizationId",
                table: "EquipmentModels",
                column: "ProposedByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_UpdatedByAppUserId",
                table: "EquipmentModels",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentItemPhotos");

            migrationBuilder.DropTable(
                name: "EquipmentItems");

            migrationBuilder.DropTable(
                name: "EquipmentModels");

            migrationBuilder.DropTable(
                name: "EquipmentBrands");

            migrationBuilder.DropTable(
                name: "EquipmentCategories");
        }
    }
}
