# Admin Role Redesign — User Management Only

**Date:** 2026-06-09  
**Status:** Approved  

## Overview

Redesign the Administrator role so it is exclusively a user management role. Remove all announcement and event create/edit/delete permissions from the admin. Add full user CRUD: add any-role user manually, edit all user fields, and hard-delete users.

## 1. Permission Stripping

### AnnouncementController
- `CanCreate()`: remove `"Administrator"` from the allowed roles list. Admin redirects away from `/Announcement/Create`.
- `CanEditAnnouncement(Announcement a)`: remove the `Administrator` bypass. Only the author (or Dean/Chair Person) can edit.

### EventController
- `CanCreate()`: remove `"Administrator"`.
- `CanManage()`: remove `"Administrator"`. Admin is no longer treated as an implicit organizer.

**Read access preserved:** `/Announcement` and `/Event` index pages have no role gate — admin can still browse as a viewer. Only write paths are closed.

## 2. Sidebar & Navigation Cleanup

### `_Layout.cshtml` — Administrator sidebar block
Keep only:
- Dashboard (`/Admin`)
- Verify Students (`/Admin/PendingUsers`)
- Manage Users (`/Admin/Users`)

Remove: Announcements, Events, QR Scanner, Group Finder, Notifications, Safety Reports, Report Safety Issue.

### Top navbar QR Scanner link
Remove `"Administrator"` from the condition that shows the QR Scanner link. It remains visible for Faculty, Dean, Chair Person.

## 3. Admin Dashboard Redesign

### `AdminController.Index()`
Replace all announcement-related ViewBag data with user-centric data:

**Stat cards:**
- Total Verified Users
- Pending Verifications (warning highlight when > 0)
- Count per role: Faculty, Dean, Chair Person, Staff, Student

**Chart data:**
- Bar chart: new user registrations per month for the last 6 months (by `CreatedAt`)
- Doughnut chart: user distribution by role

**Table/panel data:**
- Recent pending verifications: top 5, ordered by `CreatedAt` ascending
- Recently added users: last 5 with `VerificationStatus == "Verified"`, ordered by `VerifiedAt` descending

### `Admin/Index.cshtml`
Replace:
- Announcement stat cards → role-breakdown stat cards
- Monthly announcements line chart → monthly registrations bar chart
- By-category doughnut chart → users-by-role doughnut chart
- Recent announcements table → recently added users table
- Pending users alert panel → keep (already user-focused)

Quick Actions:
- Verify Students → `/Admin/PendingUsers`
- Manage Users → `/Admin/Users`
- Add User → `/Admin/AddUser`

Remove: "New Announcement" quick action button.

## 4. New User CRUD Actions

### Add User

**GET `/Admin/AddUser`**  
Renders a form with fields: First Name, Last Name, Email, Password, Student ID (optional), Role (dropdown from `Roles` table), Department (dropdown from `DepartmentTags`, marks as primary), Verification Status hardcoded to "Verified", `IsActive = true`.

**POST `/Admin/AddUser`**  
- Validates email uniqueness.
- BCrypt-hashes the password.
- Creates `User` record with `VerificationStatus = "Verified"`, `IsActive = true`, `VerifiedByID = adminID`, `VerifiedAt = DateTime.Now`.
- Creates a `UserDepartments` entry with `IsPrimary = true` for the selected department.
- Sends a welcome email (fire-and-forget).
- Redirects to `/Admin/Users` on success.

Admin-created accounts bypass the pending/approval flow entirely.

### Edit User

**GET `/Admin/EditUser/{id}`**  
Pre-populates all fields from the existing `User` record: First Name, Last Name, Email, Student ID, Role, primary Department, IsActive. Password field is blank (leave blank = keep existing hash).

**POST `/Admin/EditUser/{id}`**  
- Validates email uniqueness (excluding the current user's own email).
- If password field is non-empty, re-hashes and updates; otherwise keeps existing `PasswordHash`.
- Replaces the existing primary `UserDepartments` entry with the newly selected department.
- Updates `UpdatedAt = DateTime.Now`.
- Redirects to `/Admin/Users` on success.

Consolidates the existing `ChangeUserRole` and `ToggleUserActive` into the edit form — those standalone POST endpoints can be removed or kept as convenience endpoints for the toggle button on the list view.

### Delete User

**POST `/Admin/DeleteUser/{id}`**  
- Guard: admin cannot delete their own account (compare `id` with session `UserID`).
- Guard: if the user has any authored `Announcements` or organized `Events`, block deletion and return a TempData error ("Cannot delete a user who has authored announcements or organized events"). All FK relationships are `DeleteBehavior.Restrict` — EF will not cascade anything.
- Otherwise, manually delete child records in this order before deleting the `User`:
  1. `UserDepartments` where `UserID = id`
  2. `EventRegistrations` where `UserID = id`
  3. `EventWaitlist` where `UserID = id`
  4. `OrgMembers` where `UserID = id`
  5. `StudyGroupMembers` where `UserID = id`
  6. `Notifications` where `UserID = id`
  7. `UserAnnouncementInteractions` where `UserID = id`
  8. `User` record itself
- Wrap in a single `SaveChangesAsync()` call after all removals.
- Confirmation UI: Bootstrap modal on the `Admin/Users` list view (no separate confirm page).
- Redirects to `/Admin/Users` on success with a TempData success message.

### `Admin/Users.cshtml` updates
- Replace the existing role-change dropdown and toggle-active button per row with: **Edit** button (→ `/Admin/EditUser/{id}`) and **Delete** button (opens Bootstrap confirm modal).
- Toggle-active can remain as a quick inline button if desired, or be folded into Edit.

## 5. Files Touched

| File | Change |
|------|--------|
| `Controllers/AnnouncementController.cs` | Remove `Administrator` from `CanCreate()`, `CanEditAnnouncement()` |
| `Controllers/EventController.cs` | Remove `Administrator` from `CanCreate()`, `CanManage()` |
| `Controllers/AdminController.cs` | Rewrite `Index()` data; add `AddUser` GET+POST, `EditUser` GET+POST, `DeleteUser` POST |
| `Views/Admin/Index.cshtml` | Full redesign to user-centric dashboard |
| `Views/Admin/Users.cshtml` | Add Edit + Delete buttons per row, delete confirm modal |
| `Views/Admin/AddUser.cshtml` | New view |
| `Views/Admin/EditUser.cshtml` | New view |
| `Views/Shared/_Layout.cshtml` | Trim admin sidebar; remove admin from QR Scanner nav condition |

## 6. Constraints & Notes

- No ASP.NET Identity — auth remains session-based BCrypt as per project convention.
- All controller actions follow the existing `if (!IsAdmin()) return RedirectToAction("Login", "Account")` pattern.
- Email sending is fire-and-forget (log failures, don't throw).
- Hardcoded `localhost:7135` URLs in email bodies are an existing issue — out of scope for this change.
- EF cascade behavior must be verified against `ApplicationDbContext.OnModelCreating` before the `DeleteUser` action is implemented.
