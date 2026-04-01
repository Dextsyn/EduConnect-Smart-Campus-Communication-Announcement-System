using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnouncementCategories",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    IconName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsEmergency = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementCategories", x => x.CategoryID);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RoleLevel = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CanPublish = table.Column<bool>(type: "bit", nullable: false),
                    CanManageUsers = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleID);
                });

            migrationBuilder.CreateTable(
                name: "TagTypes",
                columns: table => new
                {
                    TagTypeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTypes", x => x.TagTypeID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StudentID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RoleID = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ProfilePicture = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLogin = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserID);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleID",
                        column: x => x.RoleID,
                        principalTable: "Roles",
                        principalColumn: "RoleID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentTags",
                columns: table => new
                {
                    TagID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TagTypeID = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentTags", x => x.TagID);
                    table.ForeignKey(
                        name: "FK_DepartmentTags_TagTypes_TagTypeID",
                        column: x => x.TagTypeID,
                        principalTable: "TagTypes",
                        principalColumn: "TagTypeID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    AnnouncementID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthorID = table.Column<int>(type: "int", nullable: false),
                    CategoryID = table.Column<int>(type: "int", nullable: false),
                    FeedType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AISummary = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    AttachmentURL = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsEmergency = table.Column<bool>(type: "bit", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.AnnouncementID);
                    table.ForeignKey(
                        name: "FK_Announcements_AnnouncementCategories_CategoryID",
                        column: x => x.CategoryID,
                        principalTable: "AnnouncementCategories",
                        principalColumn: "CategoryID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Announcements_Users_AuthorID",
                        column: x => x.AuthorID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    LogID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TableAffected = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordID = table.Column<int>(type: "int", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatbotConversations",
                columns: table => new
                {
                    ConversationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    SessionToken = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BotResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntentDetected = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WasHelpful = table.Column<bool>(type: "bit", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatbotConversations", x => x.ConversationID);
                    table.ForeignKey(
                        name: "FK_ChatbotConversations_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    TokenID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeviceInfo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.TokenID);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserDepartments",
                columns: table => new
                {
                    UserDepartmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    TagID = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDepartments", x => x.UserDepartmentID);
                    table.ForeignKey(
                        name: "FK_UserDepartments_DepartmentTags_TagID",
                        column: x => x.TagID,
                        principalTable: "DepartmentTags",
                        principalColumn: "TagID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDepartments_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AIProcessingLogs",
                columns: table => new
                {
                    AILogID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementID = table.Column<int>(type: "int", nullable: true),
                    ProcessType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InputText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModelUsed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TokensUsed = table.Column<int>(type: "int", nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIProcessingLogs", x => x.AILogID);
                    table.ForeignKey(
                        name: "FK_AIProcessingLogs_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementTags",
                columns: table => new
                {
                    AnnouncementTagID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementID = table.Column<int>(type: "int", nullable: false),
                    TagID = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementTags", x => x.AnnouncementTagID);
                    table.ForeignKey(
                        name: "FK_AnnouncementTags_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementTags_DepartmentTags_TagID",
                        column: x => x.TagID,
                        principalTable: "DepartmentTags",
                        principalColumn: "TagID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    EventID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementID = table.Column<int>(type: "int", nullable: true),
                    OrganizerID = table.Column<int>(type: "int", nullable: false),
                    EventTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StartDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxAttendees = table.Column<int>(type: "int", nullable: true),
                    IsOnline = table.Column<bool>(type: "bit", nullable: false),
                    MeetingURL = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.EventID);
                    table.ForeignKey(
                        name: "FK_Events_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID");
                    table.ForeignKey(
                        name: "FK_Events_Users_OrganizerID",
                        column: x => x.OrganizerID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Feedback",
                columns: table => new
                {
                    FeedbackID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementID = table.Column<int>(type: "int", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    FeedbackText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    SentimentScore = table.Column<decimal>(type: "decimal(4,3)", nullable: true),
                    SentimentLabel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedback", x => x.FeedbackID);
                    table.ForeignKey(
                        name: "FK_Feedback_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Feedback_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    AnnouncementID = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationID);
                    table.ForeignKey(
                        name: "FK_Notifications_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIProcessingLogs_AnnouncementID",
                table: "AIProcessingLogs",
                column: "AnnouncementID");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementCategories_CategoryName",
                table: "AnnouncementCategories",
                column: "CategoryName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_AuthorID",
                table: "Announcements",
                column: "AuthorID");

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_CategoryID",
                table: "Announcements",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTags_AnnouncementID_TagID",
                table: "AnnouncementTags",
                columns: new[] { "AnnouncementID", "TagID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTags_TagID",
                table: "AnnouncementTags",
                column: "TagID");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserID",
                table: "AuditLogs",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_ChatbotConversations_UserID",
                table: "ChatbotConversations",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentTags_TagName",
                table: "DepartmentTags",
                column: "TagName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentTags_TagTypeID",
                table: "DepartmentTags",
                column: "TagTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_Events_AnnouncementID",
                table: "Events",
                column: "AnnouncementID");

            migrationBuilder.CreateIndex(
                name: "IX_Events_OrganizerID",
                table: "Events",
                column: "OrganizerID");

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_AnnouncementID",
                table: "Feedback",
                column: "AnnouncementID");

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_UserID",
                table: "Feedback",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_AnnouncementID",
                table: "Notifications",
                column: "AnnouncementID");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserID",
                table: "Notifications",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserID",
                table: "RefreshTokens",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagTypes_TypeName",
                table: "TagTypes",
                column: "TypeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDepartments_TagID",
                table: "UserDepartments",
                column: "TagID");

            migrationBuilder.CreateIndex(
                name: "IX_UserDepartments_UserID_TagID",
                table: "UserDepartments",
                columns: new[] { "UserID", "TagID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleID",
                table: "Users",
                column: "RoleID");

            // ─── Seed Roles ────────────────────────────
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "RoleName", "RoleLevel", "Description", "CanPublish", "CanManageUsers", "CreatedAt" },
                values: new object[,]
                {
        { "Student",       1, "Can view announcements and give feedback", false, false, DateTime.Now },
        { "Faculty",       2, "Can publish academic announcements",       true,  false, DateTime.Now },
        { "Staff",         2, "Can publish non-academic announcements",   true,  false, DateTime.Now },
        { "Administrator", 4, "Full system access",                       true,  true,  DateTime.Now }
                }
            );

            // ─── Seed TagTypes ──────────────────────────
            migrationBuilder.InsertData(
                table: "TagTypes",
                columns: new[] { "TypeName", "Description", "CreatedAt" },
                values: new object[,]
                {
        { "Academic",    "College and department related announcements", DateTime.Now },
        { "NonAcademic", "Office and administrative announcements",      DateTime.Now },
        { "Emergency",   "Urgent campus wide alerts",                    DateTime.Now }
                }
            );

            // ─── Seed DepartmentTags — Academic ─────────
            migrationBuilder.InsertData(
                table: "DepartmentTags",
                columns: new[] { "TagName", "ShortName", "TagTypeID", "Description", "ColorHex", "IsActive", "CreatedAt" },
                values: new object[,]
                {
        { "School Wide",                                   "ALL",  1, "Announcements for all students and staff",     "#1E40AF", true, DateTime.Now },
        { "College of Engineering",                        "COE",  1, "Engineering department announcements",         "#B45309", true, DateTime.Now },
        { "College of Nursing",                            "CON",  1, "Nursing department announcements",             "#0F766E", true, DateTime.Now },
        { "College of Pharmacy",                           "COP",  1, "Pharmacy department announcements",            "#6D28D9", true, DateTime.Now },
        { "College of Architecture",                       "COA",  1, "Architecture department announcements",        "#92400E", true, DateTime.Now },
        { "College of Liberal Arts & Sciences",            "CLAS", 1, "Liberal Arts and Sciences announcements",      "#0369A1", true, DateTime.Now },
        { "College of Business Administration",            "CBA",  1, "Business Administration announcements",        "#065F46", true, DateTime.Now },
        { "College of Education",                          "COED", 1, "Education department announcements",           "#78350F", true, DateTime.Now },
        { "College of Law",                                "LAW",  1, "Law department announcements",                 "#1E3A5F", true, DateTime.Now },
        { "College of Computing & Information Technology", "CCIT", 1, "CCIT department announcements",                "#1D4ED8", true, DateTime.Now },
        { "College of Science",                            "COS",  1, "Science department announcements",             "#065F46", true, DateTime.Now },
        { "Physical Education Department",                 "PE",   1, "PE department announcements",                  "#15803D", true, DateTime.Now },
        { "Graduate School",                               "GS",   1, "Graduate school announcements",                "#7E22CE", true, DateTime.Now }
                }
            );

            // ─── Seed DepartmentTags — NonAcademic ──────
            migrationBuilder.InsertData(
                table: "DepartmentTags",
                columns: new[] { "TagName", "ShortName", "TagTypeID", "Description", "ColorHex", "IsActive", "CreatedAt" },
                values: new object[,]
                {
        { "Accounting Office", "ACCTG",  2, "Payment deadlines, tuition fees, billing",     "#B45309", true, DateTime.Now },
        { "Registrar Office",  "REG",    2, "Enrollment schedules, grades, records",         "#0F766E", true, DateTime.Now },
        { "Campus Store",      "STORE",  2, "Sales, promos, merchandise",                    "#7C3AED", true, DateTime.Now },
        { "Library",           "LIB",    2, "Book availability, library hours, fines",       "#1E40AF", true, DateTime.Now },
        { "Clinic",            "CLINIC", 2, "Health advisories, medical services",           "#DC2626", true, DateTime.Now },
        { "Student Affairs",   "OSA",    2, "Student concerns, scholarships, activities",    "#0369A1", true, DateTime.Now },
        { "Campus Security",   "SEC",    2, "Safety announcements, lost and found",          "#374151", true, DateTime.Now }
                }
            );

            // ─── Seed DepartmentTags — Emergency ────────
            migrationBuilder.InsertData(
                table: "DepartmentTags",
                columns: new[] { "TagName", "ShortName", "TagTypeID", "Description", "ColorHex", "IsActive", "CreatedAt" },
                values: new object[,]
                {
        { "Emergency", "EMRG", 3, "Urgent campus wide emergency alerts", "#DC2626", true, DateTime.Now }
                }
            );

            // ─── Seed AnnouncementCategories ────────────
            migrationBuilder.InsertData(
                table: "AnnouncementCategories",
                columns: new[] { "CategoryName", "Description", "ColorHex", "IconName", "IsEmergency", "IsActive", "CreatedAt" },
                values: new object[,]
                {
        { "Academic",        "Class schedules, exams, grades",        "#3B82F6", "fa-book",        false, true, DateTime.Now },
        { "Extracurricular", "Clubs, sports, campus events",          "#10B981", "fa-star",        false, true, DateTime.Now },
        { "Administrative",  "School policies, office memos",         "#8B5CF6", "fa-building",    false, true, DateTime.Now },
        { "Financial",       "Payments, tuition, billing",            "#F59E0B", "fa-money-bill",  false, true, DateTime.Now },
        { "Health",          "Health advisories, clinic updates",     "#EF4444", "fa-heart-pulse", false, true, DateTime.Now },
        { "General",         "General campus information",            "#64748B", "fa-info-circle", false, true, DateTime.Now },
        { "Emergency",       "Urgent campus wide alerts",             "#DC2626", "fa-exclamation", true,  true, DateTime.Now }
                }
            );

            // ─── Seed Admin User ─────────────────────────
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "FirstName", "LastName", "Email", "PasswordHash", "RoleID", "IsActive", "CreatedAt" },
                values: new object[]
                {
        "System", "Administrator", "admin@educonnect.edu",
        "REPLACE_WITH_BCRYPT_HASH", 4, true, DateTime.Now
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIProcessingLogs");

            migrationBuilder.DropTable(
                name: "AnnouncementTags");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ChatbotConversations");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Feedback");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "UserDepartments");

            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "DepartmentTags");

            migrationBuilder.DropTable(
                name: "AnnouncementCategories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "TagTypes");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
