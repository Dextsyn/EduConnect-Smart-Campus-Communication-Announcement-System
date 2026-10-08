/*
    Read-only report: accounts saved before the name and student number
    rules (PersonInput) that don't meet them. Changes nothing.

    The app lets an admin keep editing these accounts; a field only has to
    be fixed when someone changes it. Fix them from Manage Users > Edit.

    Run against local or Azure SQL:
      sqlcmd -S <server> -d EduConnectDB -i 2026-10-invalid-names-and-student-numbers.sql

    Each query approximates the app's rule (T-SQL LIKE has no regex):
      names        letters (accented Latin included), with single spaces,
                   hyphens, apostrophes or periods between letters
      student no.  exactly 9 digits, students only, one account each
                   (rejected registrations don't count)
*/

SET NOCOUNT ON;

PRINT 'Names with characters other than letters, space, hyphen, apostrophe, period:';
SELECT u.UserID, r.RoleName, u.FirstName, u.LastName, u.Email
FROM Users u JOIN Roles r ON r.RoleID = u.RoleID
WHERE u.FirstName COLLATE Latin1_General_BIN LIKE N'%[^A-Za-zÀ-ÖØ-öø-ÿ .''’-]%'
   OR u.LastName  COLLATE Latin1_General_BIN LIKE N'%[^A-Za-zÀ-ÖØ-öø-ÿ .''’-]%'
ORDER BY u.UserID;

PRINT 'Names that start with a separator, end with one other than a period, or double one up:';
SELECT u.UserID, r.RoleName, u.FirstName, u.LastName, u.Email
FROM Users u JOIN Roles r ON r.RoleID = u.RoleID
CROSS APPLY (VALUES (u.FirstName), (u.LastName)) n(Name)
WHERE n.Name LIKE N'[ .''’-]%'
   OR n.Name LIKE N'%[ ''’-]'
   OR n.Name LIKE N'%[ ''’-][ .''’-]%'
   OR n.Name LIKE N'%.[.''’-]%'
   OR n.Name LIKE N'%.  %'
GROUP BY u.UserID, r.RoleName, u.FirstName, u.LastName, u.Email
ORDER BY u.UserID;

PRINT 'Student numbers that are not exactly 9 digits:';
SELECT u.UserID, r.RoleName, u.StudentID, u.FirstName, u.LastName, u.VerificationStatus
FROM Users u JOIN Roles r ON r.RoleID = u.RoleID
WHERE u.StudentID IS NOT NULL
  AND (LEN(u.StudentID) <> 9 OR u.StudentID LIKE '%[^0-9]%')
ORDER BY u.UserID;

PRINT 'Non-students holding a student number (cleared the next time they are edited):';
SELECT u.UserID, r.RoleName, u.StudentID, u.FirstName, u.LastName
FROM Users u JOIN Roles r ON r.RoleID = u.RoleID
WHERE u.StudentID IS NOT NULL
  AND r.RoleName NOT IN ('Student', 'Student Pending')
ORDER BY u.UserID;

PRINT 'Student numbers held by more than one account (rejected registrations excluded).';
PRINT 'These must be resolved before the unique index can be added:';
SELECT u.StudentID, u.UserID, r.RoleName, u.FirstName, u.LastName, u.Email,
       u.VerificationStatus, u.IsActive, u.CreatedAt
FROM Users u JOIN Roles r ON r.RoleID = u.RoleID
WHERE u.StudentID IN (
    SELECT StudentID FROM Users
    WHERE StudentID IS NOT NULL AND VerificationStatus <> 'Rejected'
    GROUP BY StudentID HAVING COUNT(*) > 1)
  AND u.VerificationStatus <> 'Rejected'
ORDER BY u.StudentID, u.CreatedAt;
