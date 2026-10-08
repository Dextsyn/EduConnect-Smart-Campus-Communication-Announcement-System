using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingSanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportingSanctions",
                columns: table => new
                {
                    SanctionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    ReportID = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Strike = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuedByID = table.Column<int>(type: "int", nullable: true),
                    IssuedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferredToOsa = table.Column<bool>(type: "bit", nullable: false),
                    ReportStatusBefore = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AppealText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppealedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByID = table.Column<int>(type: "int", nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppealUpheldAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppealUpheldByID = table.Column<int>(type: "int", nullable: true),
                    AppealUpheldNote = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingSanctions", x => x.SanctionID);
                    table.ForeignKey(
                        name: "FK_ReportingSanctions_IncidentReports_ReportID",
                        column: x => x.ReportID,
                        principalTable: "IncidentReports",
                        principalColumn: "ReportID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportingSanctions_Users_AppealUpheldByID",
                        column: x => x.AppealUpheldByID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportingSanctions_Users_IssuedByID",
                        column: x => x.IssuedByID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportingSanctions_Users_RevokedByID",
                        column: x => x.RevokedByID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportingSanctions_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSanctions_AppealUpheldByID",
                table: "ReportingSanctions",
                column: "AppealUpheldByID");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSanctions_IssuedByID",
                table: "ReportingSanctions",
                column: "IssuedByID");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSanctions_ReportID",
                table: "ReportingSanctions",
                column: "ReportID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSanctions_RevokedByID",
                table: "ReportingSanctions",
                column: "RevokedByID");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSanctions_UserID_IssuedAt",
                table: "ReportingSanctions",
                columns: new[] { "UserID", "IssuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportingSanctions");
        }
    }
}
