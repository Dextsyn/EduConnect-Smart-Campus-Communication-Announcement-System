using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementApprovalFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChairApprovedAt",
                table: "Announcements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChairApprovedByID",
                table: "Announcements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChairRejectionReason",
                table: "Announcements",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_ChairApprovedByID",
                table: "Announcements",
                column: "ChairApprovedByID");

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_Users_ChairApprovedByID",
                table: "Announcements",
                column: "ChairApprovedByID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Announcements_Users_ChairApprovedByID",
                table: "Announcements");

            migrationBuilder.DropIndex(
                name: "IX_Announcements_ChairApprovedByID",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "ChairApprovedAt",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "ChairApprovedByID",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "ChairRejectionReason",
                table: "Announcements");
        }
    }
}
