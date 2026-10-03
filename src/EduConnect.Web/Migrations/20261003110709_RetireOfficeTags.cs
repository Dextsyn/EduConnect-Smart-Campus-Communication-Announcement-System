using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class RetireOfficeTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Tags & Offices page is gone; School Wide ("ALL") is the
            // only tag still in use. Keep the office rows (FKs are Restrict
            // and history may reference them) but stop them counting.
            // Azure SQL's clock is UTC; the app stores Manila time.
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 0, UpdatedAt = DATEADD(hour, 8, SYSUTCDATETIME())
                WHERE IsActive = 1 AND ShortName <> 'ALL';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The offices that were active when this ran (Library was
            // already retired).
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 1, UpdatedAt = DATEADD(hour, 8, SYSUTCDATETIME())
                WHERE ShortName IN ('ACCTG', 'REG', 'STORE', 'CLINIC', 'OSA', 'SEC');
            ");
        }
    }
}
