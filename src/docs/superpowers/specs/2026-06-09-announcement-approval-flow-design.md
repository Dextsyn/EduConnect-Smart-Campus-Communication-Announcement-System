# Announcement Approval Flow Design

**Date:** 2026-06-09  
**Status:** Approved  
**Scope:** Faculty → Chair Person → Dean two-stage approval before Faculty can publish

---

## Overview

Faculty members can currently not create announcements. This feature adds a structured approval pipeline: Faculty drafts an announcement, submits it for review, it passes through the Chair Person of their department then the Dean, and once both approve, Faculty can publish it themselves.

Admin, Dean, and Chair Person keep their existing direct-publish behavior. Staff and Students are unchanged.

---

## State Machine

The `ApprovalStatus` column on the `Announcements` table drives the pipeline. Values:

| Value | Meaning |
|---|---|
| `Draft` | Saved by Faculty, not yet submitted |
| `PendingChair` | Submitted; waiting for Chair Person in Faculty's department |
| `PendingDean` | Chair approved (or skipped); waiting for Dean in Faculty's department |
| `Approved` | Dean approved; Faculty may now publish |
| `Rejected` | Rejected by Chair or Dean; Faculty may edit and resubmit from the beginning |

The `Status` field (`Draft` / `Published`) is unchanged. Faculty sets `Status = "Published"` themselves via a dedicated Publish action, only allowed when `ApprovalStatus == "Approved"`.

On rejection, Faculty can edit the announcement and resubmit — which restarts the chain from the Chair step.

---

## Schema Changes

One new EF Core migration adds three columns to the `Announcements` table:

```csharp
// Tracks Chair Person approval
public int? ChairApprovedByID { get; set; }
public DateTime? ChairApprovedAt { get; set; }
public string? ChairRejectionReason { get; set; }
```

Existing columns are repurposed (no rename):
- `ApprovedByID` / `ApprovedAt` → Dean's approval record
- `RejectionReason` → Dean's rejection reason
- `SubmittedAt` → set when Faculty submits (already exists)

New navigation property on `Announcement`:
```csharp
public User? ChairApprovedBy { get; set; }
```

---

## Role & Access Rules

| Role | Can Create | Approval Required | Can Review | Can Publish |
|---|---|---|---|---|
| Faculty | Yes | Yes (Chair → Dean) | No | Own announcements when `ApprovalStatus == "Approved"` |
| Chair Person | Yes | No | Dept announcements in `PendingChair` | Own + direct-create |
| Dean | Yes | No | Dept announcements in `PendingDean` | Own + direct-create |
| Administrator | Yes | No | — | All |
| Staff / Student | No | — | No | No |

**Department matching:** On submit, the system resolves the Faculty's primary department tag via `UserDepartments WHERE IsPrimary = true`. It then queries for a user with `RoleName = "Chair Person"` sharing that `TagID`. If none found, it skips directly to the Dean with the same tag. If no Dean is found either, submission is blocked with an error message.

---

## New Controller Actions

All actions are added to `AnnouncementController`. Session-based role checks follow the existing pattern (no `[Authorize]` attributes).

### `POST /Announcement/Submit/{id}`
- Guard: logged-in Faculty, announcement belongs to them, `ApprovalStatus` is `Draft` or `Rejected`
- Finds Chair Person by primary department tag; falls back to Dean if none; blocks with error if neither exists
- Clears `ChairRejectionReason`, `RejectionReason`, `ChairApprovedByID`, `ChairApprovedAt`, `ApprovedByID`, `ApprovedAt` (stale data from prior rejected cycle)
- Sets `ApprovalStatus = "PendingChair"` (or `"PendingDean"` if skipping), sets `SubmittedAt`
- Sends email + in-app notification to the reviewer
- Redirects to `MyAnnouncements`

### `POST /Announcement/Publish/{id}`
- Guard: logged-in Faculty, announcement belongs to them, `ApprovalStatus == "Approved"`
- Sets `Status = "Published"`, `PublishedAt = DateTime.Now`
- Sends real-time in-app notifications to department members (same logic as current direct-publish)
- Redirects to `MyAnnouncements`

### `GET /Announcement/MyAnnouncements`
- Guard: Faculty only
- Lists all announcements authored by the current user
- Ordered by `CreatedAt` descending
- Passes status, rejection reasons, and action availability to the view

### `GET /Announcement/ReviewQueue`
- Guard: Chair Person or Dean only
- Chair Person sees `ApprovalStatus == "PendingChair"` announcements tagged to their primary department
- Dean sees `ApprovalStatus == "PendingDean"` announcements tagged to their primary department
- Ordered by `SubmittedAt` ascending (oldest first)

### `GET /Announcement/Review/{id}`
- Guard: Chair Person or Dean, announcement must be in the correct pending state for their role and in their department
- Returns full announcement details (read-only) with approve/reject form

### `POST /Announcement/Approve/{id}`
- Guard: Chair Person or Dean, correct pending state, correct department
- **Chair Person:** sets `ApprovalStatus = "PendingDean"`, records `ChairApprovedByID` / `ChairApprovedAt`, notifies Dean (email + in-app)
- **Dean:** sets `ApprovalStatus = "Approved"`, records `ApprovedByID` / `ApprovedAt`, notifies Faculty (email + in-app)
- Redirects to `ReviewQueue`

### `POST /Announcement/Reject/{id}`
- Guard: Chair Person or Dean, correct pending state, correct department
- Rejection reason is required (validated server-side)
- **Chair Person:** sets `ApprovalStatus = "Rejected"`, fills `ChairRejectionReason`, notifies Faculty (email + in-app)
- **Dean:** sets `ApprovalStatus = "Rejected"`, fills `RejectionReason`, notifies Faculty (email + in-app)
- Redirects to `ReviewQueue`

---

## Changes to Existing Actions

### `GET /Announcement/Create` and `POST /Announcement/Create`
- `CanPublish()` helper renamed / replaced: Faculty is now allowed to reach the Create form
- Faculty path saves `Status = "Draft"`, `ApprovalStatus = "Draft"` — no publish on create
- The form renders a **"Save Draft"** button for Faculty (instead of "Publish")
- Admin / Dean / Chair Person path unchanged — still publishes immediately

### `GET /Announcement/Edit`
- Faculty may edit only when `ApprovalStatus` is `Draft` or `Rejected`
- Editing a `Rejected` announcement resets `ApprovalStatus` to `Draft` (must re-submit)

---

## Views

### `Views/Announcement/MyAnnouncements.cshtml`
Faculty-only. Table with columns: Title, Feed Type, Status badge, Submitted date, Action buttons.

Status badge colors:
- Grey → `Draft`
- Yellow → `PendingChair`
- Orange → `PendingDean`
- Green → `Approved`
- Red → `Rejected`

Action buttons per row (shown conditionally):
- **Edit** — Draft or Rejected
- **Submit for Review** — Draft only (POST to `/Announcement/Submit/{id}`)
- **Publish** — Approved only (POST to `/Announcement/Publish/{id}`)

Rejected rows expand inline to show the rejection reason (either `ChairRejectionReason` or `RejectionReason`).

### `Views/Announcement/ReviewQueue.cshtml`
Shared by Chair Person and Dean (role-aware heading). Table with: Author name, Title, Feed Type, Department tag, Submitted date, "Review" link. Empty state if no items pending.

### `Views/Announcement/Review.cshtml`
Full read-only announcement preview (title, body, category, tags, feed type, author). Below the preview:
- **Approve** button (POST to `/Announcement/Approve/{id}`)
- **Reject** section: textarea (required, placeholder "Provide a reason for rejection") + Reject button (POST to `/Announcement/Reject/{id}`)

---

## Notifications

All notifications use the existing `INotificationService` (in-app) and `IEmailService` (email, fire-and-forget).

| Trigger | Recipient | In-App Message | Email Subject |
|---|---|---|---|
| Faculty submits → Chair | Chair Person | "New announcement pending your review: {title}" | "EduConnect: Announcement Pending Review" |
| Faculty submits → Dean (skip) | Dean | "New announcement pending your review: {title}" | "EduConnect: Announcement Pending Review" |
| Chair approves → Dean | Dean | "Announcement forwarded for your review: {title}" | "EduConnect: Announcement Pending Your Approval" |
| Chair rejects → Faculty | Faculty | "Your announcement was rejected by the Chair Person" | "EduConnect: Announcement Rejected" |
| Dean approves → Faculty | Faculty | "Your announcement has been approved — you can now publish it" | "EduConnect: Announcement Approved" |
| Dean rejects → Faculty | Faculty | "Your announcement was rejected by the Dean" | "EduConnect: Announcement Rejected" |

Email bodies include a direct link to the relevant action page (`/Announcement/ReviewQueue` for reviewers, `/Announcement/MyAnnouncements` for Faculty). Links use `https://localhost:7135/...` consistent with existing email bodies.

---

## Nav Integration

The Chair Person and Dean sidebar layouts get a **"Pending Reviews"** nav link pointing to `/Announcement/ReviewQueue`. A badge next to the link shows the count of items currently awaiting their review (queried on each page load, same as the existing notification badge pattern).

The Faculty sidebar gets a **"My Announcements"** nav link pointing to `/Announcement/MyAnnouncements`.

---

## Out of Scope

- Staff creating announcements (no change)
- Admin bypassing the chain (Admins publish directly, unchanged)
- Chair Person or Dean editing a Faculty's announcement during review (read-only)
- Escalation timers or auto-escalation if reviewer is inactive
