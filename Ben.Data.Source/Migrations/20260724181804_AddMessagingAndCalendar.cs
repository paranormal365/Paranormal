using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingAndCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrgCalendarEventTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ColorClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgCalendarEventTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventTypes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventTypes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventTypes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChannelType = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEncrypted = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMessages_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessages_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessages_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessages_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessages_OrgMessages_ParentMessageId",
                        column: x => x.ParentMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessages_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgCalendarEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    StartDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAllDay = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    RecurrenceRule = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgCalendarEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEvents_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEvents_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEvents_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEvents_OrgCalendarEventTypes_EventTypeId",
                        column: x => x.EventTypeId,
                        principalTable: "OrgCalendarEventTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgMessageRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateRead = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMessageRecipients_AppUsers_RecipientAppUserId",
                        column: x => x.RecipientAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageRecipients_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgMessageViews",
                columns: table => new
                {
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateViewed = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageViews", x => new { x.OrgMessageId, x.ViewerAppUserId });
                    table.ForeignKey(
                        name: "FK_OrgMessageViews_AppUsers_ViewerAppUserId",
                        column: x => x.ViewerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageViews_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgCalendarEventAttendees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RsvpStatus = table.Column<int>(type: "int", nullable: false),
                    AssignedTask = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DateRsvp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgCalendarEventAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventAttendees_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventAttendees_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventAttendees_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_AppUserId",
                table: "OrgCalendarEventAttendees",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_CreatedByAppUserId",
                table: "OrgCalendarEventAttendees",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_OrgCalendarEventId_AppUserId",
                table: "OrgCalendarEventAttendees",
                columns: new[] { "OrgCalendarEventId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_CaseId",
                table: "OrgCalendarEvents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_CreatedByAppUserId",
                table: "OrgCalendarEvents",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_EventTypeId",
                table: "OrgCalendarEvents",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_OrganizationId",
                table: "OrgCalendarEvents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_UpdatedByAppUserId",
                table: "OrgCalendarEvents",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventTypes_CreatedByAppUserId",
                table: "OrgCalendarEventTypes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventTypes_OrganizationId",
                table: "OrgCalendarEventTypes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventTypes_UpdatedByAppUserId",
                table: "OrgCalendarEventTypes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageRecipients_OrgMessageId_RecipientAppUserId",
                table: "OrgMessageRecipients",
                columns: new[] { "OrgMessageId", "RecipientAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageRecipients_RecipientAppUserId",
                table: "OrgMessageRecipients",
                column: "RecipientAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_AuthorAppUserId",
                table: "OrgMessages",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_CaseId",
                table: "OrgMessages",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_CreatedByAppUserId",
                table: "OrgMessages",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_OrganizationId",
                table: "OrgMessages",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_ParentMessageId",
                table: "OrgMessages",
                column: "ParentMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_UpdatedByAppUserId",
                table: "OrgMessages",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageViews_ViewerAppUserId",
                table: "OrgMessageViews",
                column: "ViewerAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgCalendarEventAttendees");

            migrationBuilder.DropTable(
                name: "OrgMessageRecipients");

            migrationBuilder.DropTable(
                name: "OrgMessageViews");

            migrationBuilder.DropTable(
                name: "OrgCalendarEvents");

            migrationBuilder.DropTable(
                name: "OrgMessages");

            migrationBuilder.DropTable(
                name: "OrgCalendarEventTypes");
        }
    }
}
