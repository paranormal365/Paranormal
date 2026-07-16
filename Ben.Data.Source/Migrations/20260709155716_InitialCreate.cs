using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationAddressTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAddressTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAddressTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddressTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationEmailTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEmailTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationEmailTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationEmailTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationLinkTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationLinkTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationLinkTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLinkTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationNoteTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationNoteTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationNoteTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationNoteTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationPhoneTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationPhoneTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationPhoneTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPhoneTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UrlName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organizations_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Organizations_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserAddressTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAddressTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAddressTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAddressTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserEmailTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEmailTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserEmailTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserEmailTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserLinkTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLinkTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLinkTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserLinkTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserMessageTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMessageTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMessageTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserMessageTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNoteTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNoteTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNoteTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserNoteTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPhoneTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPhoneTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPhoneTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserPhoneTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationAddressTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StreetAddress1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StreetAddress2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAddresses_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddresses_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddresses_OrganizationAddressTypes_OrganizationAddressTypeId",
                        column: x => x.OrganizationAddressTypeId,
                        principalTable: "OrganizationAddressTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddresses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationEmailTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    DateValidated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidationToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationEmails_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationEmails_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationEmails_OrganizationEmailTypes_OrganizationEmailTypeId",
                        column: x => x.OrganizationEmailTypeId,
                        principalTable: "OrganizationEmailTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationEmails_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationLinkTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsVerifiedApproved = table.Column<bool>(type: "bit", nullable: false),
                    DateVerifiedApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationLinks_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLinks_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLinks_AppUsers_VerifiedApprovedByAppUserId",
                        column: x => x.VerifiedApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLinks_OrganizationLinkTypes_OrganizationLinkTypeId",
                        column: x => x.OrganizationLinkTypeId,
                        principalTable: "OrganizationLinkTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLinks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationNoteTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TableName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoteBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoteSubject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationNotes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationNotes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationNotes_OrganizationNoteTypes_OrganizationNoteTypeId",
                        column: x => x.OrganizationNoteTypeId,
                        principalTable: "OrganizationNoteTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationNotes_OrganizationNotes_ParentNoteId",
                        column: x => x.ParentNoteId,
                        principalTable: "OrganizationNotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationNotes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsHome = table.Column<bool>(type: "bit", nullable: false),
                    PageTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UrlName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PageHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationPages_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPages_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPages_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationPhones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationPhoneTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateValidated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PhoneCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsCellular = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationPhones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationPhones_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPhones_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPhones_OrganizationPhoneTypes_OrganizationPhoneTypeId",
                        column: x => x.OrganizationPhoneTypeId,
                        principalTable: "OrganizationPhoneTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationPhones_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAddressTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StreetAddress1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StreetAddress2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAddresses_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAddresses_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAddresses_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAddresses_UserAddressTypes_UserAddressTypeId",
                        column: x => x.UserAddressTypeId,
                        principalTable: "UserAddressTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserEmailTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    ValidationToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateValidated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserEmails_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserEmails_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserEmails_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserEmails_UserEmailTypes_UserEmailTypeId",
                        column: x => x.UserEmailTypeId,
                        principalTable: "UserEmailTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserLinkTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsVerifiedApproved = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateVerifiedApproved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLinks_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserLinks_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserLinks_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserLinks_AppUsers_VerifiedApprovedByAppUserId",
                        column: x => x.VerifiedApprovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserLinks_UserLinkTypes_UserLinkTypeId",
                        column: x => x.UserLinkTypeId,
                        principalTable: "UserLinkTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserMessageTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageSubject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateArchived = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMessages_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserMessages_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserMessages_UserMessageTypes_UserMessageTypeId",
                        column: x => x.UserMessageTypeId,
                        principalTable: "UserMessageTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserMessages_UserMessages_ParentMessageId",
                        column: x => x.ParentMessageId,
                        principalTable: "UserMessages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserNoteTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteSubject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoteBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParentNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TableName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserNotes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserNotes_UserNoteTypes_UserNoteTypeId",
                        column: x => x.UserNoteTypeId,
                        principalTable: "UserNoteTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserNotes_UserNotes_ParentNoteId",
                        column: x => x.ParentNoteId,
                        principalTable: "UserNotes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPhones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserPhoneTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsCellular = table.Column<bool>(type: "bit", nullable: false),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    ValidationToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateValidated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPhones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPhones_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserPhones_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserPhones_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserPhones_UserPhoneTypes_UserPhoneTypeId",
                        column: x => x.UserPhoneTypeId,
                        principalTable: "UserPhoneTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserMessageTos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateLastRead = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReadCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMessageTos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMessageTos_AppUsers_ToAppUserId",
                        column: x => x.ToAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserMessageTos_UserMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "UserMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddresses_CreatedByAppUserId",
                table: "OrganizationAddresses",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddresses_OrganizationAddressTypeId",
                table: "OrganizationAddresses",
                column: "OrganizationAddressTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddresses_OrganizationId",
                table: "OrganizationAddresses",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddresses_UpdatedByAppUserId",
                table: "OrganizationAddresses",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressTypes_CreatedByAppUserId",
                table: "OrganizationAddressTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressTypes_UpdatedByAppUserId",
                table: "OrganizationAddressTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmails_CreatedByAppUserId",
                table: "OrganizationEmails",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmails_OrganizationEmailTypeId",
                table: "OrganizationEmails",
                column: "OrganizationEmailTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmails_OrganizationId",
                table: "OrganizationEmails",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmails_UpdatedByAppUserId",
                table: "OrganizationEmails",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmailTypes_CreatedByAppUserId",
                table: "OrganizationEmailTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmailTypes_UpdatedByAppUserId",
                table: "OrganizationEmailTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinks_CreatedByAppUserId",
                table: "OrganizationLinks",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinks_OrganizationId",
                table: "OrganizationLinks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinks_OrganizationLinkTypeId",
                table: "OrganizationLinks",
                column: "OrganizationLinkTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinks_UpdatedByAppUserId",
                table: "OrganizationLinks",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinks_VerifiedApprovedByAppUserId",
                table: "OrganizationLinks",
                column: "VerifiedApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinkTypes_CreatedByAppUserId",
                table: "OrganizationLinkTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLinkTypes_UpdatedByAppUserId",
                table: "OrganizationLinkTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNotes_CreatedByAppUserId",
                table: "OrganizationNotes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNotes_OrganizationId",
                table: "OrganizationNotes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNotes_OrganizationNoteTypeId",
                table: "OrganizationNotes",
                column: "OrganizationNoteTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNotes_ParentNoteId",
                table: "OrganizationNotes",
                column: "ParentNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNotes_UpdatedByAppUserId",
                table: "OrganizationNotes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNoteTypes_CreatedByAppUserId",
                table: "OrganizationNoteTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationNoteTypes_UpdatedByAppUserId",
                table: "OrganizationNoteTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_CreatedByAppUserId",
                table: "OrganizationPages",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_OrganizationId",
                table: "OrganizationPages",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_UpdatedByAppUserId",
                table: "OrganizationPages",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhones_CreatedByAppUserId",
                table: "OrganizationPhones",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhones_OrganizationId",
                table: "OrganizationPhones",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhones_OrganizationPhoneTypeId",
                table: "OrganizationPhones",
                column: "OrganizationPhoneTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhones_UpdatedByAppUserId",
                table: "OrganizationPhones",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhoneTypes_CreatedByAppUserId",
                table: "OrganizationPhoneTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPhoneTypes_UpdatedByAppUserId",
                table: "OrganizationPhoneTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CreatedByAppUserId",
                table: "Organizations",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_UpdatedByAppUserId",
                table: "Organizations",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_AppUserId",
                table: "UserAddresses",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_CreatedByAppUserId",
                table: "UserAddresses",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_UpdatedByAppUserId",
                table: "UserAddresses",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_UserAddressTypeId",
                table: "UserAddresses",
                column: "UserAddressTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddressTypes_CreatedByAppUserId",
                table: "UserAddressTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddressTypes_UpdatedByAppUserId",
                table: "UserAddressTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmails_AppUserId",
                table: "UserEmails",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmails_CreatedByAppUserId",
                table: "UserEmails",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmails_UpdatedByAppUserId",
                table: "UserEmails",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmails_UserEmailTypeId",
                table: "UserEmails",
                column: "UserEmailTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmailTypes_CreatedByAppUserId",
                table: "UserEmailTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmailTypes_UpdatedByAppUserId",
                table: "UserEmailTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinks_AppUserId",
                table: "UserLinks",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinks_CreatedByAppUserId",
                table: "UserLinks",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinks_UpdatedByAppUserId",
                table: "UserLinks",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinks_UserLinkTypeId",
                table: "UserLinks",
                column: "UserLinkTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinks_VerifiedApprovedByAppUserId",
                table: "UserLinks",
                column: "VerifiedApprovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinkTypes_CreatedByAppUserId",
                table: "UserLinkTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLinkTypes_UpdatedByAppUserId",
                table: "UserLinkTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_CreatedByAppUserId",
                table: "UserMessages",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_ParentMessageId",
                table: "UserMessages",
                column: "ParentMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_UpdatedByAppUserId",
                table: "UserMessages",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_UserMessageTypeId",
                table: "UserMessages",
                column: "UserMessageTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageTos_MessageId",
                table: "UserMessageTos",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageTos_ToAppUserId",
                table: "UserMessageTos",
                column: "ToAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageTypes_CreatedByAppUserId",
                table: "UserMessageTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageTypes_UpdatedByAppUserId",
                table: "UserMessageTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_CreatedByAppUserId",
                table: "UserNotes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_ParentNoteId",
                table: "UserNotes",
                column: "ParentNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UpdatedByAppUserId",
                table: "UserNotes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UserNoteTypeId",
                table: "UserNotes",
                column: "UserNoteTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNoteTypes_CreatedByAppUserId",
                table: "UserNoteTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNoteTypes_UpdatedByAppUserId",
                table: "UserNoteTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhones_AppUserId",
                table: "UserPhones",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhones_CreatedByAppUserId",
                table: "UserPhones",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhones_UpdatedByAppUserId",
                table: "UserPhones",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhones_UserPhoneTypeId",
                table: "UserPhones",
                column: "UserPhoneTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhoneTypes_CreatedByAppUserId",
                table: "UserPhoneTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPhoneTypes_UpdatedByAppUserId",
                table: "UserPhoneTypes",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationAddresses");

            migrationBuilder.DropTable(
                name: "OrganizationEmails");

            migrationBuilder.DropTable(
                name: "OrganizationLinks");

            migrationBuilder.DropTable(
                name: "OrganizationNotes");

            migrationBuilder.DropTable(
                name: "OrganizationPages");

            migrationBuilder.DropTable(
                name: "OrganizationPhones");

            migrationBuilder.DropTable(
                name: "UserAddresses");

            migrationBuilder.DropTable(
                name: "UserEmails");

            migrationBuilder.DropTable(
                name: "UserLinks");

            migrationBuilder.DropTable(
                name: "UserMessageTos");

            migrationBuilder.DropTable(
                name: "UserNotes");

            migrationBuilder.DropTable(
                name: "UserPhones");

            migrationBuilder.DropTable(
                name: "OrganizationAddressTypes");

            migrationBuilder.DropTable(
                name: "OrganizationEmailTypes");

            migrationBuilder.DropTable(
                name: "OrganizationLinkTypes");

            migrationBuilder.DropTable(
                name: "OrganizationNoteTypes");

            migrationBuilder.DropTable(
                name: "OrganizationPhoneTypes");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "UserAddressTypes");

            migrationBuilder.DropTable(
                name: "UserEmailTypes");

            migrationBuilder.DropTable(
                name: "UserLinkTypes");

            migrationBuilder.DropTable(
                name: "UserMessages");

            migrationBuilder.DropTable(
                name: "UserNoteTypes");

            migrationBuilder.DropTable(
                name: "UserPhoneTypes");

            migrationBuilder.DropTable(
                name: "UserMessageTypes");

            migrationBuilder.DropTable(
                name: "AppUsers");
        }
    }
}
