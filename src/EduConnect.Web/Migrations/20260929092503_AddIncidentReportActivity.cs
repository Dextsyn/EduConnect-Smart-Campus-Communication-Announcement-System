using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentReportActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncidentReportActivities",
                columns: table => new
                {
                    ActivityID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportID = table.Column<int>(type: "int", nullable: false),
                    ActorID = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentReportActivities", x => x.ActivityID);
                    table.ForeignKey(
                        name: "FK_IncidentReportActivities_IncidentReports_ReportID",
                        column: x => x.ReportID,
                        principalTable: "IncidentReports",
                        principalColumn: "ReportID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentReportActivities_Users_ActorID",
                        column: x => x.ActorID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentReportActivities_ActorID_CreatedAt",
                table: "IncidentReportActivities",
                columns: new[] { "ActorID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentReportActivities_ReportID",
                table: "IncidentReportActivities",
                column: "ReportID");

            // Backfill: every existing report was received, and a handled
            // one gets the only change we can still reconstruct — its
            // current status and note, credited to whoever handled it.
            migrationBuilder.Sql(@"
INSERT INTO IncidentReportActivities
    (ReportID, ActorID, ActorName, Action, FromStatus, ToStatus, Note, CreatedAt)
SELECT r.ReportID,
       CASE WHEN r.IsAnonymous = 1 THEN NULL ELSE r.ReportedByID END,
       CASE WHEN r.IsAnonymous = 1 THEN 'Anonymous'
            ELSE u.FirstName + ' ' + u.LastName END,
       'Received', NULL, 'Pending', NULL, r.ReportedAt
FROM IncidentReports r
LEFT JOIN Users u ON u.UserID = r.ReportedByID;

INSERT INTO IncidentReportActivities
    (ReportID, ActorID, ActorName, Action, FromStatus, ToStatus, Note, CreatedAt)
SELECT r.ReportID, r.HandledByID, h.FirstName + ' ' + h.LastName,
       CASE WHEN r.Status = 'Pending' THEN 'NoteUpdated' ELSE 'StatusChanged' END,
       'Pending', r.Status, r.Resolution, COALESCE(r.ResolvedAt, r.ReportedAt)
FROM IncidentReports r
JOIN Users h ON h.UserID = r.HandledByID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentReportActivities");
        }
    }
}
