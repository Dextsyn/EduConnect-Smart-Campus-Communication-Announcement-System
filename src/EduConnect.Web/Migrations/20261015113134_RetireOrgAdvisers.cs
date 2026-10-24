using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RetireOrgAdvisers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Faculty advisers no longer post for organizations — student
            // Organization Officers do, assigned by an admin. The rows are
            // kept (inactive) so the advisers' old posts keep their history.
            migrationBuilder.Sql(
                "UPDATE OrgMembers SET IsActive = 0 WHERE OrgRole = 'Adviser' AND IsActive = 1;");

            // A student is an officer of one organization at a time.
            migrationBuilder.CreateIndex(
                name: "IX_OrgMembers_OneOfficerOrgPerUser",
                table: "OrgMembers",
                column: "UserID",
                unique: true,
                filter: "[OrgRole] = 'Officer' AND [IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The retired adviser rows are not reactivated: which of them
            // were active before can't be told apart from earlier swaps.
            migrationBuilder.DropIndex(
                name: "IX_OrgMembers_OneOfficerOrgPerUser",
                table: "OrgMembers");
        }
    }
}
