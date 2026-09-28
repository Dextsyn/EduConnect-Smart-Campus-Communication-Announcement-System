using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameChairPersonToChairperson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keyed on RoleName: RoleID is an identity column and differs
            // between databases. Guarded so a re-run is a no-op.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Chairperson')
                UPDATE Roles
                SET RoleName = 'Chairperson',
                    Description = 'Reviews and publishes announcements for their department',
                    UpdatedAt = SYSDATETIME()
                WHERE RoleName = 'Chair Person';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Roles
                SET RoleName = 'Chair Person',
                    Description = 'Can create announcements and events for their department',
                    UpdatedAt = SYSDATETIME()
                WHERE RoleName = 'Chairperson';
            ");
        }
    }
}
