# User Profile Editor — Design Spec
Date: 2026-07-10

## Overview

Add a self-service profile page at `/Account/Profile` for all authenticated roles. Users can upload a profile picture, set a name suffix, and change their password. Name, Student No., and Email are read-only. The nav and sidebar avatar updates to reflect the uploaded photo everywhere.

---

## 1. Data Model

### Migration: `AddUserSuffix`
Add one column to `Users`:

| Column  | Type          | Nullable | Constraint     |
|---------|---------------|----------|----------------|
| Suffix  | nvarchar(10)  | YES      | —              |

The `ProfilePicture` column (`nvarchar(500)`) already exists on `User` and is already in the database — no additional migration needed for it.

No other schema changes.

---

## 2. ViewModels

**`ProfileViewModel`** (`ViewModel/ProfileViewModel.cs`)

| Property            | Type          | Notes                                          |
|---------------------|---------------|------------------------------------------------|
| FullName            | string        | Read-only display — FirstName + Suffix + LastName |
| StudentID           | string?       | Read-only display; null for non-student roles  |
| Email               | string        | Read-only display                              |
| RoleName            | string        | Read-only display                              |
| ProfilePicturePath  | string?       | Current `/uploads/avatars/…` path, or null     |
| Suffix              | string?       | Editable; bound to the suffix dropdown         |
| NewProfilePicture   | IFormFile?    | File upload; optional on every save            |

**`ChangePasswordViewModel`** (`ViewModel/ChangePasswordViewModel.cs`)

| Property        | Type   | Validation                          |
|-----------------|--------|-------------------------------------|
| CurrentPassword | string | Required                            |
| NewPassword     | string | Required, MinLength(8)              |
| ConfirmPassword | string | Required, [Compare("NewPassword")]  |

---

## 3. Controller Actions (`AccountController`)

All actions guard the session the same way every other controller does — check `HttpContext.Session.GetString("UserID")` and redirect to login if null.

### `GET /Account/Profile`
1. Read `UserID` from session.
2. Load `User` from DB.
3. Build and return `ProfileViewModel`.

### `POST /Account/Profile`
Editable fields accepted: `Suffix`, `NewProfilePicture`. All other fields are ignored even if present in the form.

1. Guard session.
2. If `NewProfilePicture` is supplied:
   - Validate extension: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp` only.
   - Validate size: ≤ 5 MB.
   - Save to `wwwroot/uploads/avatars/<guid><ext>`.
   - Delete the old avatar file if one exists (prevents orphan files).
   - Update `user.ProfilePicture` with the new relative path `/uploads/avatars/<file>`.
3. Update `user.Suffix` from the form value (null when "None" is selected).
4. Set `user.UpdatedAt = DateTime.Now`.
5. Save changes.
6. Update session key `ProfilePicture` with the new path (or clear it if none).
7. `TempData["Success"] = "Profile updated."` and redirect to `GET /Account/Profile`.

On validation failure: re-populate `ProfileViewModel` from DB and return the view with `ModelState` errors.

### `POST /Account/ChangePassword`
1. Guard session.
2. Load user from DB.
3. Verify `model.CurrentPassword` against `user.PasswordHash` via `BCrypt.Net.BCrypt.Verify`. If wrong: `ModelState.AddModelError("CurrentPassword", "Current password is incorrect.")` and return the view.
4. Hash `model.NewPassword` and store in `user.PasswordHash`.
5. `user.UpdatedAt = DateTime.Now`.
6. Save changes.
7. `TempData["Success"] = "Password changed successfully."` and redirect to `GET /Account/Profile`.

---

## 4. File Upload Convention

Follows the existing event cover-photo pattern exactly:

- Folder: `wwwroot/uploads/avatars/` — created via `Directory.CreateDirectory` if absent.
- Filename: `Guid.NewGuid().ToString() + extension` (lowercased).
- Old file deleted before writing new one (file-system cleanup, not DB-level).
- Allowed extensions: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`.
- Max size: 5 MB.
- Errors added to `ModelState` on violation; view re-rendered with existing profile data intact.

---

## 5. Session Keys

Add one session key `ProfilePicture` (the relative URL or empty string). Set on login and on every successful profile save. Used by `_Layout.cshtml` and `_SidebarContent.cshtml` for the avatar `<img>`.

In `AccountController.Login` (POST), after saving the session, store:
```csharp
HttpContext.Session.SetString("ProfilePicture", user.ProfilePicture ?? "");
```

---

## 6. View — `Views/Account/Profile.cshtml`

Single page, no layout override (uses `_Layout.cshtml`). Two Bootstrap cards stacked vertically, consistent with the existing ec-* design system (navy `#002F6C`, gold `#F2A900`, Bootstrap 5.1 classes only).

### Card 1 — Profile Info

- **Avatar section**: circular `<img>` (128×128) showing the uploaded photo when `ProfilePicturePath` is set, otherwise the ui-avatars fallback `https://ui-avatars.com/api/?name=<FullName>&background=002F6C&color=fff`. Below the avatar: a file input labeled "Change profile picture".
- **Read-only fields** (rendered as disabled `<input>` elements so the layout is uniform):
  - Full Name (FirstName + current Suffix + LastName)
  - Student No. — row hidden (`d-none`) when `StudentID` is null
  - Email
  - Role
- **Editable field**:
  - Suffix — `<select>` with options: None / Jr. / Sr. / II / III / IV / V. Pre-selects the current value.
- Save button: `ec-btn-primary` (navy).

### Card 2 — Change Password

- Current Password (password input)
- New Password (password input, MinLength 8 enforced client-side via `minlength`)
- Confirm New Password (password input)
- Submit button: `ec-btn-primary`.
- Posts to `POST /Account/ChangePassword`.

### TempData alerts

A single alert block at the top of the page renders `TempData["Success"]` (green) or `TempData["Error"]` (red) using the same pattern as other views.

---

## 7. Avatar Update in Shared Partials

### `_Layout.cshtml` (navbar avatar, line ~86)
Replace the static ui-avatars `<img>` with:
```csharp
@{
    var picPath = Context.Session.GetString("ProfilePicture");
    var avatarSrc = string.IsNullOrEmpty(picPath)
        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(Context.Session.GetString("UserName") ?? "")}&background=002F6C&color=fff"
        : picPath;
}
<img src="@avatarSrc" class="rounded-circle ec-avatar-ring" width="32" height="32" alt="Avatar" />
```

### `_SidebarContent.cshtml`
Apply the same avatar-source logic wherever the user avatar is rendered in the sidebar.

---

## 8. Suffix Display in Session Name

The session key `UserName` stores `FirstName + " " + LastName`. The suffix is **not** appended to `UserName` in session (keeps it simple). The suffix appears in the Profile page's read-only Full Name display and can be included in display-name rendering on the profile page itself.

---

## 9. Out of Scope

- Email change (would need verification flow).
- First/Last name change (admin-only concern).
- Student ID change (admin-only concern).
- Cropping or resizing images server-side.
- Multiple profile photos / photo history.
