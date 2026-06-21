# Event Edit & Delete — Design Spec

**Date:** 2026-06-21
**Status:** Approved

---

## Overview

Add Edit and Delete actions to events so that the faculty member, dean, or chair person who created an event can manage it after creation. Only the event creator may edit or delete their own events. Deleting an event with existing registrants cancels it (sets `Status = "Cancelled"`) and emails all registered attendees.

---

## Authorization Model

Two distinct permission checks are used in `EventController`:

| Check | Rule | Used for |
|---|---|---|
| `IsCreator(Event ev)` | `ev.OrganizerID == GetUserID()` | Edit, Delete |
| `ViewBag.IsOrganizer` (existing) | `OrganizerID == userID \|\| role == Administrator` | View Registrants |

`ViewBag.IsCreator` (new, pure creator check) is passed to Details so the view can gate the Edit and Delete buttons independently from the Registrants link.

Admins can still view registrants for any event but cannot edit or delete events they did not create.

---

## Controller Changes — `EventController`

### New helper

```csharp
private bool IsCreator(Event ev) =>
    ev.OrganizerID == GetUserID();
```

### GET /Event/Edit/{id}

1. Auth: `IsLoggedIn()` + `CanManageEvents()` + `IsCreator(ev)`. Return 403/redirect if not creator.
2. Load event from DB.
3. Populate `EventFormViewModel`:
   - All existing field values pre-filled.
   - `ExistingCoverPhotoURL` set from `ev.CoverPhotoURL`.
   - `Announcements` dropdown loaded (published announcements by current user).
4. Return `Edit` view.

### POST /Event/Edit/{id}

1. Auth: same as GET.
2. Reload event from DB (re-check creator in case of concurrent access).
3. Run same date validations as Create (start in future, end after start).
4. Photo handling:
   - If `model.RemoveCoverPhoto == true`: delete file, set `CoverPhotoURL = null`.
   - Else if new file uploaded: validate type + size, delete old file, save new file.
   - Else: keep existing `CoverPhotoURL` unchanged.
5. Update all event fields. Set `ev.UpdatedAt = DateTime.Now`.
6. `SaveChangesAsync()`. Redirect to `Details/{id}` with `TempData["Success"]`.

### POST /Event/Delete/{id}

1. Auth: `IsLoggedIn()` + `IsCreator(ev)`.
2. Load event with `Registrations` included.
3. Set `ev.Status = "Cancelled"`, `ev.IsRegistrationOpen = false`, `ev.UpdatedAt = DateTime.Now`.
4. `SaveChangesAsync()`.
5. Email all registrants whose `Status == "Registered"` (fire-and-forget, failures logged).
   - Subject: `"Event Cancelled: {EventTitle}"`
   - Body: brief HTML notice with event name, original date, and a message that the event has been cancelled.
6. Redirect to `Index` with `TempData["Success"] = "Event cancelled and registrants notified."`.

---

## ViewModel Changes

### `EventListViewModel`

Add one property:

```csharp
public int OrganizerID { get; set; }
```

Populated in `EventController.Index` from `e.OrganizerID`.

### `EventFormViewModel`

Add one property to support the Edit photo UI:

```csharp
public bool RemoveCoverPhoto { get; set; }
```

(The `ExistingCoverPhotoURL` property already exists on the VM.)

---

## View Changes

### `Views/Event/Edit.cshtml` (new)

- Cloned structure from `Create.cshtml`.
- Title: "Edit Event".
- All inputs pre-filled via model binding.
- Cover photo section shows existing photo thumbnail when `Model.ExistingCoverPhotoURL != null`, with a "Remove photo" checkbox (`asp-for="RemoveCoverPhoto"`). When checked via JS, the file input is hidden and the thumbnail dims.
- Form posts to `POST /Event/Edit/{id}`.
- Same JS validations as Create (image type guard on file input, date pickers).

### `Views/Event/Details.cshtml` (modified)

- Add `ViewBag.IsCreator` bool (set in controller).
- "Edit Event" button already present — change its condition from `isOrganizer` to `isCreator`.
- Add "Delete Event" danger button below Edit, also gated by `isCreator`.
- Delete button opens a Bootstrap modal (`id="deleteEventModal"`) with:
  - Warning message: "This will cancel the event and notify all registered attendees. This cannot be undone."
  - Confirm button submits a hidden `<form method="post" action="/Event/Delete/{id}">` with antiforgery token.
- "View Registrants" link stays gated by `isOrganizer` (unchanged).

### `Views/Event/Index.cshtml` (modified)

- Read `ViewBag.CurrentUserID` (int, set in controller).
- On each event card, when `ev.OrganizerID == currentUserID`, render two small icon buttons in the card's top-right corner:
  - Pencil icon → `href="/Event/Edit/{id}"` (outline-secondary, sm).
  - Trash icon → opens inline confirm modal for that event (same modal pattern as Details).
- Each card gets its own delete modal (`id="deleteModal-{EventID}"`) to avoid ID collisions when multiple cards are on-screen.

---

## No Migration Required

All required columns (`Status`, `IsRegistrationOpen`, `UpdatedAt`) already exist on the `Events` table. No schema changes needed.

---

## File Checklist

| File | Change |
|---|---|
| `EventController.cs` | Add `IsCreator()`, GET/POST Edit, POST Delete, set `ViewBag.IsCreator` + `ViewBag.CurrentUserID` in Index/Details |
| `ViewModel/EventFormViewModel.cs` | Add `RemoveCoverPhoto` bool |
| `ViewModel/EventFormViewModel.cs` | Add `OrganizerID` to `EventListViewModel` |
| `Views/Event/Edit.cshtml` | New view |
| `Views/Event/Details.cshtml` | Add Delete button + modal, switch Edit gate to `IsCreator` |
| `Views/Event/Index.cshtml` | Add Edit/Delete icon buttons on creator's cards |
