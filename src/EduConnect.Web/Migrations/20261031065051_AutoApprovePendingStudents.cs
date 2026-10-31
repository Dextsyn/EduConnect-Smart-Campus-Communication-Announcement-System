using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduConnect.Web.Migrations
{
    /// <inheritdoc />
    public partial class AutoApprovePendingStudents : Migration
    {
        // Confirming the Adamson email now verifies a student, so the ITC
        // queue is gone. Students who had confirmed and were still waiting
        // are verified the same way: no VerifiedByID, so they show in
        // Recent Registrations. Unconfirmed registrations are left alone.
        // Azure SQL's clock is UTC; the app stores Manila time.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @now datetime2 = DATEADD(hour, 8, SYSUTCDATETIME());

INSERT INTO AuditLogs (UserID, ActorName, Action, TableAffected, RecordID, Summary, CreatedAt)
SELECT NULL, 'System', 'Verify', 'Users', u.UserID,
       LEFT(u.FirstName + ' ' + u.LastName + ' (' + u.Email + ') was waiting for ITC; verified automatically when email confirmation replaced the ITC review.', 500),
       @now
FROM Users u
WHERE u.VerificationStatus = 'Pending';

UPDATE Users
SET VerificationStatus = 'Verified',
    RoleID = (SELECT RoleID FROM Roles WHERE RoleName = 'Student'),
    IsActive = 1,
    VerifiedByID = NULL,
    VerifiedAt = @now,
    UpdatedAt = @now
WHERE VerificationStatus = 'Pending';
");
        }

        // There is no way back to the queue: which students were waiting
        // isn't recorded anywhere but the audit log.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
