using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAdviserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adds the Organization Adviser role row only — deliberately inert.
            // No user holds the role yet and no application code references it,
            // so this is safe to deploy ahead of the feature that uses it.
            // Converting the sitting advisers is a separate migration that ships
            // with that code; doing it here would leave them holding a role
            // nothing handles.
            //
            // RoleLevel / CanPublish / CanManageUsers are never read by the
            // application (authorization compares RoleName); they are set only
            // to keep the row shaped like its neighbours.
            //
            // Keyed on RoleName, never on RoleID: RoleID is an identity column
            // and will not match between this database and Azure.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Organization Adviser')
                INSERT INTO Roles
                    (RoleName, RoleLevel, Description,
                     CanPublish, CanManageUsers, CreatedAt)
                VALUES
                    ('Organization Adviser', 2,
                     'Posts announcements for a single student organization',
                     0, 0, SYSDATETIME());
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Safe to delete outright: Users.RoleID references Roles, so if
            // anyone has been given the role this will fail loudly rather than
            // orphan them.
            migrationBuilder.Sql(
                "DELETE FROM Roles WHERE RoleName = 'Organization Adviser';");
        }
    }
}
