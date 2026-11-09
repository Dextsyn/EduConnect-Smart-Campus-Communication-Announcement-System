using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AllowOfficersOfSeveralOrgs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A student can now be an officer of several organizations
            // (2026-11-09), so the one-officer-org-per-student index goes.
            migrationBuilder.DropIndex(
                name: "IX_OrgMembers_OneOfficerOrgPerUser",
                table: "OrgMembers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrgMembers_OneOfficerOrgPerUser",
                table: "OrgMembers",
                column: "UserID",
                unique: true,
                filter: "[OrgRole] = 'Officer' AND [IsActive] = 1");
        }
    }
}
