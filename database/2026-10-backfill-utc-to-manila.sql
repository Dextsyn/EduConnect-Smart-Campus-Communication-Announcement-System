/*
    One-off fix for PRODUCTION (Azure SQL) only - do not run against a local
    database whose clock was already Manila time.

    Until WEBSITE_TIME_ZONE was set on the App Service, the server clock was
    UTC, so every timestamp the app stamped with DateTime.Now was written
    8 hours behind Manila time. This adds 8 hours to those values.

    How to run:
      1. Set @SwitchManila to the Manila date/time you saved WEBSITE_TIME_ZONE
         in the Azure portal (round UP to be safe - a value a little late is
         fine, a value too early misses rows).
      2. Run as is (@Apply = 0): a dry run that prints how many rows each
         column would change, then rolls back.
      3. Review the counts, set @Apply = 1 and run again to commit.

    The script records itself in dbo.__TimestampBackfill and refuses to run
    a second time, because a second run would shift old rows again.

    Not touched, on purpose:
      - Events.StartDateTime / EndDateTime / RegistrationDeadline,
        Announcements.ExpiresAt, OrgAnnouncements.ExpiresAt: typed in by
        users as Manila times, never server-stamped.
      - Groups / GroupMembers / GroupMessages: stamped with DateTime.UtcNow
        and meant to stay UTC.
      - PasswordResetTokens, RefreshTokens: short-lived.
      - Roles, TagTypes, AnnouncementCategories: seed data.
      - StudyGroups*, Members, Orders: not used by the app.
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- sqlcmd defaults it OFF; filtered indexes need it ON

DECLARE @SwitchManila datetime2 = NULL;   -- e.g. '2026-10-03 16:00'
DECLARE @Apply        bit       = 0;      -- 0 = dry run, 1 = commit

IF @SwitchManila IS NULL
BEGIN
    RAISERROR('Set @SwitchManila first.', 16, 1);
    RETURN;
END

IF OBJECT_ID('dbo.__TimestampBackfill') IS NOT NULL
BEGIN
    RAISERROR('This backfill has already been applied (see dbo.__TimestampBackfill).', 16, 1);
    RETURN;
END

-- Rows stamped before the switch hold UTC values at or before this instant;
-- rows stamped after it hold Manila values at least 8 hours later, so the
-- cutoff cannot catch a new row.
DECLARE @CutoffUtc datetime2 = DATEADD(hour, -8, @SwitchManila);
PRINT CONCAT('Cutoff (UTC, as stored): ', CONVERT(varchar(30), @CutoffUtc, 120));
PRINT '';

BEGIN TRANSACTION;

UPDATE AIProcessingLogs SET ProcessedAt = DATEADD(hour, 8, ProcessedAt) WHERE ProcessedAt <= @CutoffUtc;
PRINT CONCAT('AIProcessingLogs.ProcessedAt            ', @@ROWCOUNT);

UPDATE Announcements SET CreatedAt       = DATEADD(hour, 8, CreatedAt)       WHERE CreatedAt       <= @CutoffUtc;
PRINT CONCAT('Announcements.CreatedAt                 ', @@ROWCOUNT);
UPDATE Announcements SET UpdatedAt       = DATEADD(hour, 8, UpdatedAt)       WHERE UpdatedAt       <= @CutoffUtc;
PRINT CONCAT('Announcements.UpdatedAt                 ', @@ROWCOUNT);
UPDATE Announcements SET SubmittedAt     = DATEADD(hour, 8, SubmittedAt)     WHERE SubmittedAt     <= @CutoffUtc;
PRINT CONCAT('Announcements.SubmittedAt               ', @@ROWCOUNT);
UPDATE Announcements SET ChairApprovedAt = DATEADD(hour, 8, ChairApprovedAt) WHERE ChairApprovedAt <= @CutoffUtc;
PRINT CONCAT('Announcements.ChairApprovedAt           ', @@ROWCOUNT);
UPDATE Announcements SET ApprovedAt      = DATEADD(hour, 8, ApprovedAt)      WHERE ApprovedAt      <= @CutoffUtc;
PRINT CONCAT('Announcements.ApprovedAt                ', @@ROWCOUNT);
UPDATE Announcements SET PublishedAt     = DATEADD(hour, 8, PublishedAt)     WHERE PublishedAt     <= @CutoffUtc;
PRINT CONCAT('Announcements.PublishedAt               ', @@ROWCOUNT);

UPDATE AnnouncementTags    SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('AnnouncementTags.CreatedAt              ', @@ROWCOUNT);
UPDATE AnnouncementTargets SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('AnnouncementTargets.CreatedAt           ', @@ROWCOUNT);

UPDATE AuditLogs            SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('AuditLogs.CreatedAt                     ', @@ROWCOUNT);
UPDATE ChatbotConversations SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('ChatbotConversations.CreatedAt          ', @@ROWCOUNT);

UPDATE Colleges    SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Colleges.CreatedAt                      ', @@ROWCOUNT);
UPDATE Colleges    SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Colleges.UpdatedAt                      ', @@ROWCOUNT);
UPDATE Colleges    SET RetiredAt = DATEADD(hour, 8, RetiredAt) WHERE RetiredAt <= @CutoffUtc;
PRINT CONCAT('Colleges.RetiredAt                      ', @@ROWCOUNT);
UPDATE Departments SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Departments.CreatedAt                   ', @@ROWCOUNT);
UPDATE Departments SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Departments.UpdatedAt                   ', @@ROWCOUNT);
UPDATE Departments SET RetiredAt = DATEADD(hour, 8, RetiredAt) WHERE RetiredAt <= @CutoffUtc;
PRINT CONCAT('Departments.RetiredAt                   ', @@ROWCOUNT);
UPDATE Programs    SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Programs.CreatedAt                      ', @@ROWCOUNT);
UPDATE Programs    SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Programs.UpdatedAt                      ', @@ROWCOUNT);
UPDATE Programs    SET RetiredAt = DATEADD(hour, 8, RetiredAt) WHERE RetiredAt <= @CutoffUtc;
PRINT CONCAT('Programs.RetiredAt                      ', @@ROWCOUNT);

UPDATE DepartmentTags  SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('DepartmentTags.CreatedAt                ', @@ROWCOUNT);
UPDATE DepartmentTags  SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('DepartmentTags.UpdatedAt                ', @@ROWCOUNT);
UPDATE UserDepartments SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('UserDepartments.CreatedAt               ', @@ROWCOUNT);

UPDATE Events SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Events.CreatedAt                        ', @@ROWCOUNT);
UPDATE Events SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Events.UpdatedAt                        ', @@ROWCOUNT);

UPDATE EventRegistrations SET RegisteredAt = DATEADD(hour, 8, RegisteredAt) WHERE RegisteredAt <= @CutoffUtc;
PRINT CONCAT('EventRegistrations.RegisteredAt         ', @@ROWCOUNT);
UPDATE EventRegistrations SET UpdatedAt    = DATEADD(hour, 8, UpdatedAt)    WHERE UpdatedAt    <= @CutoffUtc;
PRINT CONCAT('EventRegistrations.UpdatedAt            ', @@ROWCOUNT);
UPDATE EventRegistrations SET CheckedInAt  = DATEADD(hour, 8, CheckedInAt)  WHERE CheckedInAt  <= @CutoffUtc;
PRINT CONCAT('EventRegistrations.CheckedInAt          ', @@ROWCOUNT);
UPDATE EventWaitlist      SET JoinedAt     = DATEADD(hour, 8, JoinedAt)     WHERE JoinedAt     <= @CutoffUtc;
PRINT CONCAT('EventWaitlist.JoinedAt                  ', @@ROWCOUNT);

UPDATE Feedback SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Feedback.CreatedAt                      ', @@ROWCOUNT);
UPDATE Feedback SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Feedback.UpdatedAt                      ', @@ROWCOUNT);

UPDATE IncidentReports          SET ReportedAt = DATEADD(hour, 8, ReportedAt) WHERE ReportedAt <= @CutoffUtc;
PRINT CONCAT('IncidentReports.ReportedAt              ', @@ROWCOUNT);
UPDATE IncidentReports          SET ResolvedAt = DATEADD(hour, 8, ResolvedAt) WHERE ResolvedAt <= @CutoffUtc;
PRINT CONCAT('IncidentReports.ResolvedAt              ', @@ROWCOUNT);
UPDATE IncidentReportActivities SET CreatedAt  = DATEADD(hour, 8, CreatedAt)  WHERE CreatedAt  <= @CutoffUtc;
PRINT CONCAT('IncidentReportActivities.CreatedAt      ', @@ROWCOUNT);

UPDATE Notifications SET SentAt = DATEADD(hour, 8, SentAt) WHERE SentAt <= @CutoffUtc;
PRINT CONCAT('Notifications.SentAt                    ', @@ROWCOUNT);
UPDATE Notifications SET ReadAt = DATEADD(hour, 8, ReadAt) WHERE ReadAt <= @CutoffUtc;
PRINT CONCAT('Notifications.ReadAt                    ', @@ROWCOUNT);

UPDATE Organizations    SET CreatedAt = DATEADD(hour, 8, CreatedAt) WHERE CreatedAt <= @CutoffUtc;
PRINT CONCAT('Organizations.CreatedAt                 ', @@ROWCOUNT);
UPDATE Organizations    SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('Organizations.UpdatedAt                 ', @@ROWCOUNT);
UPDATE OrgAnnouncements SET PostedAt  = DATEADD(hour, 8, PostedAt)  WHERE PostedAt  <= @CutoffUtc;
PRINT CONCAT('OrgAnnouncements.PostedAt               ', @@ROWCOUNT);
UPDATE OrgAnnouncements SET UpdatedAt = DATEADD(hour, 8, UpdatedAt) WHERE UpdatedAt <= @CutoffUtc;
PRINT CONCAT('OrgAnnouncements.UpdatedAt              ', @@ROWCOUNT);
UPDATE OrgMembers       SET JoinedAt  = DATEADD(hour, 8, JoinedAt)  WHERE JoinedAt  <= @CutoffUtc;
PRINT CONCAT('OrgMembers.JoinedAt                     ', @@ROWCOUNT);

UPDATE UserAnnouncementInteractions SET ViewedAt = DATEADD(hour, 8, ViewedAt) WHERE ViewedAt <= @CutoffUtc;
PRINT CONCAT('UserAnnouncementInteractions.ViewedAt   ', @@ROWCOUNT);

UPDATE Users SET CreatedAt  = DATEADD(hour, 8, CreatedAt)  WHERE CreatedAt  <= @CutoffUtc;
PRINT CONCAT('Users.CreatedAt                         ', @@ROWCOUNT);
UPDATE Users SET UpdatedAt  = DATEADD(hour, 8, UpdatedAt)  WHERE UpdatedAt  <= @CutoffUtc;
PRINT CONCAT('Users.UpdatedAt                         ', @@ROWCOUNT);
UPDATE Users SET LastLogin  = DATEADD(hour, 8, LastLogin)  WHERE LastLogin  <= @CutoffUtc;
PRINT CONCAT('Users.LastLogin                         ', @@ROWCOUNT);
UPDATE Users SET VerifiedAt = DATEADD(hour, 8, VerifiedAt) WHERE VerifiedAt <= @CutoffUtc;
PRINT CONCAT('Users.VerifiedAt                        ', @@ROWCOUNT);

IF @Apply = 1
BEGIN
    CREATE TABLE dbo.__TimestampBackfill (AppliedAt datetime2 NOT NULL, CutoffUtc datetime2 NOT NULL);
    INSERT dbo.__TimestampBackfill VALUES (SYSUTCDATETIME(), @CutoffUtc);
    COMMIT TRANSACTION;
    PRINT '';
    PRINT 'Committed.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT '';
    PRINT 'Dry run - nothing changed. Set @Apply = 1 to commit.';
END
