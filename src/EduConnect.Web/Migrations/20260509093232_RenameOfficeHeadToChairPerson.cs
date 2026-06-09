using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameOfficeHeadToChairPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Roles SET RoleName = 'Chair Person', " +
                "Description = 'Can create announcements and events for their department' " +
                "WHERE RoleName = 'Office Head'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Roles SET RoleName = 'Office Head', " +
                "Description = 'Approves staff announcements' " +
                "WHERE RoleName = 'Chair Person'");
        }
    }
}
