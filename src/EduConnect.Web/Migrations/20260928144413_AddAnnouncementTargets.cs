using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnouncementTargets",
                columns: table => new
                {
                    AnnouncementTargetID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementID = table.Column<int>(type: "int", nullable: false),
                    CollegeID = table.Column<int>(type: "int", nullable: true),
                    DepartmentID = table.Column<int>(type: "int", nullable: true),
                    ProgramID = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementTargets", x => x.AnnouncementTargetID);
                    table.CheckConstraint("CK_AnnouncementTargets_OneLevel", "(CASE WHEN CollegeID IS NULL THEN 0 ELSE 1 END) + (CASE WHEN DepartmentID IS NULL THEN 0 ELSE 1 END) + (CASE WHEN ProgramID IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_AnnouncementTargets_Announcements_AnnouncementID",
                        column: x => x.AnnouncementID,
                        principalTable: "Announcements",
                        principalColumn: "AnnouncementID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementTargets_Colleges_CollegeID",
                        column: x => x.CollegeID,
                        principalTable: "Colleges",
                        principalColumn: "CollegeID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementTargets_Departments_DepartmentID",
                        column: x => x.DepartmentID,
                        principalTable: "Departments",
                        principalColumn: "DepartmentID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementTargets_Programs_ProgramID",
                        column: x => x.ProgramID,
                        principalTable: "Programs",
                        principalColumn: "ProgramID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTargets_AnnouncementID",
                table: "AnnouncementTargets",
                column: "AnnouncementID");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTargets_CollegeID",
                table: "AnnouncementTargets",
                column: "CollegeID");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTargets_DepartmentID",
                table: "AnnouncementTargets",
                column: "DepartmentID");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTargets_ProgramID",
                table: "AnnouncementTargets",
                column: "ProgramID");

            // Existing announcements were tagged with what are now colleges:
            // give each the matching college target so its audience is
            // unchanged. The legacy tag rows stay until Plan 5.
            migrationBuilder.Sql(@"
                INSERT INTO AnnouncementTargets (AnnouncementID, CollegeID, CreatedAt)
                SELECT DISTINCT at.AnnouncementID, c.CollegeID, SYSDATETIME()
                FROM AnnouncementTags at
                JOIN Colleges c ON c.LegacyTagID = at.TagID
                WHERE NOT EXISTS (
                    SELECT 1 FROM AnnouncementTargets x
                    WHERE x.AnnouncementID = at.AnnouncementID AND x.CollegeID = c.CollegeID);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnouncementTargets");
        }
    }
}
