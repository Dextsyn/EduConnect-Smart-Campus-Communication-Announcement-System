using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class ResetPendingApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Approval now routes by department and college. Anything caught
            // mid-review under the old tag-based routing goes back to Draft,
            // and its author is told to resubmit. Re-running is a no-op:
            // once reset, nothing matches.
            migrationBuilder.Sql(@"
                INSERT INTO Notifications (UserID, AnnouncementID, Type, Message, Link, IsRead, Channel, SentAt)
                SELECT a.AuthorID, a.AnnouncementID, 'AnnouncementReturned',
                       LEFT(N'Your announcement ""' + a.Title + N'"" was returned to draft because the approval process changed. Please submit it again.', 500),
                       '/Announcement/MyAnnouncements', 0, 'InApp', SYSDATETIME()
                FROM Announcements a
                WHERE a.ApprovalStatus IN ('PendingChair', 'PendingDean');

                UPDATE Announcements
                SET ApprovalStatus = 'Draft',
                    SubmittedAt = NULL,
                    ChairApprovedByID = NULL,
                    ChairApprovedAt = NULL,
                    UpdatedAt = SYSDATETIME()
                WHERE ApprovalStatus IN ('PendingChair', 'PendingDean');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the previous pending state is not recorded.
        }
    }
}
