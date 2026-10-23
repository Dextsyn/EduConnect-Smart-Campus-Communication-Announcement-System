using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class ExpireGroupChatsFromCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Group chats now expire one week after the group was created,
            // Open or Full. Before, only Full groups had a deadline, and a
            // leave/rejoin restarted it, so live groups ran for weeks.
            // Group Finder times are UTC, so SYSUTCDATETIME() is the right clock.
            migrationBuilder.Sql(
                "UPDATE Groups SET ChatExpiresAt = DATEADD(day, 7, CreatedAt) " +
                "WHERE Status <> 'Dissolved';");

            // Dissolve the ones already past it, the same way the sweep does.
            migrationBuilder.Sql(
                "DELETE m FROM GroupMessages m JOIN Groups g ON g.GroupID = m.GroupID " +
                "WHERE g.Status <> 'Dissolved' AND g.ChatExpiresAt <= SYSUTCDATETIME();");
            migrationBuilder.Sql(
                "UPDATE Groups SET Status = 'Dissolved', ChatExpiresAt = NULL " +
                "WHERE Status <> 'Dissolved' AND ChatExpiresAt <= SYSUTCDATETIME();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dissolved groups and their deleted messages can't be restored;
            // put the old rule back on the rest: only Full groups have a deadline.
            migrationBuilder.Sql(
                "UPDATE Groups SET ChatExpiresAt = NULL WHERE Status = 'Open';");
        }
    }
}
