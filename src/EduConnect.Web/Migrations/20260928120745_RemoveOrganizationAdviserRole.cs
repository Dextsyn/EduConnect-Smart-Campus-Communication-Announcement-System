using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOrganizationAdviserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An organization adviser is a Faculty member whose OrgMembers
            // row says OrgRole = 'Adviser' — not a role of its own. Anyone
            // given the short-lived role goes back to Faculty (their
            // OrgMembers rows are untouched), then the row is removed.
            // Keyed on RoleName; RoleID differs between databases.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Organization Adviser')
                BEGIN
                    UPDATE u
                    SET RoleID = f.RoleID, UpdatedAt = SYSDATETIME()
                    FROM Users u
                    JOIN Roles a ON a.RoleID = u.RoleID AND a.RoleName = 'Organization Adviser'
                    CROSS JOIN (SELECT RoleID FROM Roles WHERE RoleName = 'Faculty') f;

                    DELETE FROM Roles WHERE RoleName = 'Organization Adviser';
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the inert row only; converted users stay Faculty.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Organization Adviser')
                INSERT INTO Roles
                    (RoleName, RoleLevel, Description, CanPublish, CanManageUsers, CreatedAt)
                VALUES
                    ('Organization Adviser', 2,
                     'Posts announcements for a single student organization',
                     0, 0, SYSDATETIME());
            ");
        }
    }
}
