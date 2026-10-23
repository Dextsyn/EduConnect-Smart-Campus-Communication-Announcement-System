using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollegeID",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentID",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDepartmentOnly",
                table: "Events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Events_CollegeID",
                table: "Events",
                column: "CollegeID");

            migrationBuilder.CreateIndex(
                name: "IX_Events_DepartmentID",
                table: "Events",
                column: "DepartmentID");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Colleges_CollegeID",
                table: "Events",
                column: "CollegeID",
                principalTable: "Colleges",
                principalColumn: "CollegeID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Departments_DepartmentID",
                table: "Events",
                column: "DepartmentID",
                principalTable: "Departments",
                principalColumn: "DepartmentID",
                onDelete: ReferentialAction.Restrict);

            // Existing events are for their organizer's current college
            // (whole college). An organizer with no college leaves the
            // event visible only to them and its registrants.
            migrationBuilder.Sql(@"
UPDATE e
SET e.CollegeID = u.CollegeID,
    e.DepartmentID = u.DepartmentID
FROM Events e
JOIN Users u ON u.UserID = e.OrganizerID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Colleges_CollegeID",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Departments_DepartmentID",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_CollegeID",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_DepartmentID",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CollegeID",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DepartmentID",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsDepartmentOnly",
                table: "Events");
        }
    }
}
