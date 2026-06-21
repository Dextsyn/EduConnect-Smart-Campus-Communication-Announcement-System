# Announcement Create/Edit Redesign

**Date:** 2026-06-18  
**Status:** Approved

## Overview

Redesign the announcement creation and edit forms to eliminate the Category/FeedType redundancy, simplify priority levels, and add a controlled expiry date toggle.

## Approved Approach: A — Derive FeedType from Category

FeedType is removed as a user-facing field. Instead, each `AnnouncementCategory` row owns its `FeedType` value. When an announcement is saved, the controller reads the selected category's FeedType and writes it to `Announcements.FeedType`. The filtering index on `Announcements.FeedType` is preserved.

Category → FeedType mapping:
| Category | FeedType |
|---|---|
| Academic | Academic |
| Extracurricular | NonAcademic |
| Administrative | NonAcademic |
| Financial | NonAcademic |
| Health | NonAcademic |
| General | NonAcademic |
| Emergency | Emergency |

## Changes

### Database / EF Model
- Add `FeedType NVARCHAR(20) NOT NULL DEFAULT 'NonAcademic'` to `AnnouncementCategories` with a CHECK constraint matching the existing Announcements CHK.
- `AnnouncementCategory` model: add `public string FeedType { get; set; } = "NonAcademic";`
- EF Core migration to apply the schema change.
- Update `EduConnectDB.sql` seed data to include FeedType per category.

### ViewModel
- `AnnouncementFormViewModel`: remove `FeedType` property. `CategoryID` remains required. Controller derives FeedType internally.

### Controller
- `Create` (POST) and `Edit` (POST): after binding the model, load the `AnnouncementCategory` by `CategoryID` and set `announcement.FeedType = category.FeedType`.

### Priority
- Remove Urgent (4) and Emergency (5) options from the priority dropdown.
- Rename Normal (2) → Medium.
- Render as three visual pills: Low | Medium | High backed by hidden input `Priority = 1 | 2 | 3`.
- Existing announcements with Priority > 3 are unaffected in the DB; they will not appear in the new UI but data is not lost.

### Expiry Date
- Add a `bool ExpiryEnabled` field to the form (not persisted — just drives JS).
- A toggle switch shows/hides/enables the datetime input.
- When the toggle is off the input is disabled and grayed out; its value is cleared before submit.
- `min` attribute set to current datetime so the browser blocks past date selection.

### Views (Create.cshtml and Edit.cshtml)
- Remove the FeedType `<select>`.
- Replace Category `<select>` with a card-grid of clickable category tiles (radio-button pattern via hidden input).
- Replace Priority `<select>` with three pill buttons backed by a hidden `<input name="Priority">`.
- Replace the bare ExpiresAt input with a toggle-switch + conditionally-enabled datetime input.

## Out of Scope
- Changing how FeedType is used in the Index filter or feed tabs — those stay as-is.
- Removing the `FeedType` column from `Announcements` — it stays to preserve the index and existing data.
- Any changes to the Dean review or approval workflow.
