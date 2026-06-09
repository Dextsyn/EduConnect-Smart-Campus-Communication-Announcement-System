using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmergencyBroadcast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyAcknowledgements");

            migrationBuilder.DropTable(
                name: "EmergencyBroadcasts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmergencyBroadcasts",
                columns: table => new
                {
                    BroadcastID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SentByID = table.Column<int>(type: "int", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyBroadcasts", x => x.BroadcastID);
                    table.ForeignKey(
                        name: "FK_EmergencyBroadcasts_Users_SentByID",
                        column: x => x.SentByID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyAcknowledgements",
                columns: table => new
                {
                    AckID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BroadcastID = table.Column<int>(type: "int", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyAcknowledgements", x => x.AckID);
                    table.ForeignKey(
                        name: "FK_EmergencyAcknowledgements_EmergencyBroadcasts_BroadcastID",
                        column: x => x.BroadcastID,
                        principalTable: "EmergencyBroadcasts",
                        principalColumn: "BroadcastID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyAcknowledgements_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyAcknowledgements_BroadcastID_UserID",
                table: "EmergencyAcknowledgements",
                columns: new[] { "BroadcastID", "UserID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyAcknowledgements_UserID",
                table: "EmergencyAcknowledgements",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyBroadcasts_SentByID",
                table: "EmergencyBroadcasts",
                column: "SentByID");
        }
    }
}
