using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollegeID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgramID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_CollegeID",
                table: "Users",
                column: "CollegeID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentID",
                table: "Users",
                column: "DepartmentID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ProgramID",
                table: "Users",
                column: "ProgramID");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Colleges_CollegeID",
                table: "Users",
                column: "CollegeID",
                principalTable: "Colleges",
                principalColumn: "CollegeID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentID",
                table: "Users",
                column: "DepartmentID",
                principalTable: "Departments",
                principalColumn: "DepartmentID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Programs_ProgramID",
                table: "Users",
                column: "ProgramID",
                principalTable: "Programs",
                principalColumn: "ProgramID",
                onDelete: ReferentialAction.Restrict);

            // Existing academic users were tagged at what is really college
            // level. Carry that over; department/program placement is
            // manual (the admin assigns it, or the student picks it).
            // Users whose primary tag is not a college (ALL, offices) or who
            // have no tag keep NULL, and so do roles that are never placed
            // (Administrator, Staff) even if they carry a college tag.
            migrationBuilder.Sql(@"
                UPDATE u
                SET CollegeID = c.CollegeID
                FROM Users u
                JOIN Roles r ON r.RoleID = u.RoleID
                    AND r.RoleName IN ('Dean', 'Chairperson', 'Faculty', 'Student', 'Student Pending')
                JOIN UserDepartments ud ON ud.UserID = u.UserID AND ud.IsPrimary = 1
                JOIN Colleges c ON c.LegacyTagID = ud.TagID
                WHERE u.CollegeID IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Colleges_CollegeID",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentID",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Programs_ProgramID",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_CollegeID",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentID",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ProgramID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CollegeID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DepartmentID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProgramID",
                table: "Users");
        }
    }
}
