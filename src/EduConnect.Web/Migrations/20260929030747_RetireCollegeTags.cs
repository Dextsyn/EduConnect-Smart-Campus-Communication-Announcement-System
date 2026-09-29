using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RetireCollegeTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // College tags are colleges now (Academic Structure). Keep the
            // rows — old announcements and users still reference them — but
            // stop offering them anywhere.
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 0, UpdatedAt = SYSDATETIME()
                WHERE IsActive = 1
                  AND TagID IN (SELECT LegacyTagID FROM Colleges WHERE LegacyTagID IS NOT NULL);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 1, UpdatedAt = SYSDATETIME()
                WHERE TagID IN (SELECT LegacyTagID FROM Colleges WHERE LegacyTagID IS NOT NULL);
            ");
        }
    }
}
