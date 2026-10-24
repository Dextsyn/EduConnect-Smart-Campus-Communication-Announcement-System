# Org Follow, About Us, and Post Photos — Design

Date: 2026-10-23
Status: approved by the owner 2026-10-23
Mockups: `.superpowers/brainstorm/org-follow/org-page-v1.html` (local, gitignored)

## Why

Today every logged-in user can read every organization's announcements. The
owner wants org posts to belong to the people who care about the org: a
student who hasn't chosen to follow an org sees an introduction to it (About
Us) and a Follow button, not its posts. Following is self-service, so
students can find and join orgs' updates without waiting for an admin, while
the official member list (which officers are chosen from) stays curated by
admins. Officers, who know their org best, maintain the About Us themselves.
Posts also get an inline photo, since most org announcements are posters.

## Prerequisite: build on the officer branch

The student Organization Officer feature is **not on `main`**. It lives on
`feature/org-officers` (commit `c72fbbd`), which `main` is 5 commits ahead of.
Everything below assumes that branch: `OrgRoles`, `IOrgAccess`
(`IsOfficerAsync`, `OfficerOrgAsync`, `EligibleStudents`), `IAuditService`
with `AuditArea.Organizations`, `INotificationService` in `OrgController`, the
admin `Members` page, and the `RetireOrgAdvisers` migration.

Work goes on a new branch `feature/org-follow` cut from `feature/org-officers`
after merging `main` into it. Never push `main`; the owner merges and pushes
(main deploys to Azure). Azure will need both `RetireOrgAdvisers` and this
feature's migration applied.

## Goals

1. Org announcements are visible only to people allowed to see them (below).
2. Everyone else who opens an org sees its About Us view with a Follow button
   (students) or without one (other roles).
3. Students follow and unfollow orgs themselves, from any college.
4. A new org post notifies its members **and** its followers.
5. An org's officers can edit its About Us.
6. Org posts can carry one inline photo, separate from the existing file
   attachment.

## Non-goals

- Faculty, Staff, Dean, Chairperson and other non-student roles following
  orgs. ("For now just focus on students.")
- Follow requests, approval, or officer control over who follows.
- Changing how official membership works: admins still add/remove members
  and assign officers via `/Org/Members/{id}`.
- Officers editing anything besides About Us (name, logo, cover, college,
  active status stay Admin-only).
- Multiple photos per post.

## Anti-goals (would be a failure even if it works)

- Following puts a student into `OrgMembers` or makes them eligible to be an
  officer. Follow and membership must stay separate.
- Locking follows to the student's own college. Any student may follow any
  active org (unlike events, which are college-locked).
- Post content leaking to a non-follower anywhere in the app: the org page,
  the `/Org` list, notifications. (Uploaded files stay reachable by direct
  link, by decision #7.)

## Decisions made by the owner

| # | Decision |
|---|----------|
| 1 | Follow is **separate** from membership. Students follow themselves; admins still manage the official member list. |
| 2 | Only **students** can follow, and from **any college**. |
| 3 | Followers (and members) see the org's announcements; anyone else sees its About Us. |
| 4 | Officers assigned to the org can edit its About Us. |
| 5 | Org posts get a **photo** attachment, **one per post**. |
| 6 | The mockups (views A–E) were approved as shown. |
| 7 | Post photos and files keep **public blob URLs** (random names, no login needed to open a link), consistent with announcements, events, profiles and safety reports. No gated download. |
| 8 | **No auto-follow on deploy.** Non-member students start following nothing and lose sight of org posts until they click Follow. `/Org` shows a short hint for students who follow nothing: "Follow the organizations you care about to see their announcements." |

This overrides decision #2 of the Oct 15 officer work ("org page readable by
everyone"). Admin-managed membership (Oct 15 #8) is unchanged.

## Who sees what

"Can see posts" for org X =
- Administrator, **or**
- an active `OrgMembers` row for X (any `OrgRole` except `Adviser`; includes
  officers) on an active user, **or**
- a follow row for X, and the user is still an eligible student
  (`IOrgAccess.EligibleStudents()`: active, Student role, Verified).

| Viewer | Org page | Follow button | Posts |
|--------|----------|---------------|-------|
| Eligible student, not following, not a member | About Us view (view A) | **Follow** | Hidden, locked box |
| Student following | Announcements tab + About Us tab (view B) | **Following** (click = unfollow) | Shown |
| Student member (incl. officer) | Same as follower | **Member**, disabled | Shown |
| Officer of X | As member, plus **New Post** and About Us edit (view C) | **Member**, disabled | Shown |
| Administrator | As follower, no button | none | Shown (moderation) |
| Faculty, Staff, Dean, Chair, unverified student | About Us view | none | Hidden |

*Builder's call / owner's guess, flagged:* Admins seeing every org's posts is
an assumption the owner did not contradict; unverified students are treated
like non-students because `EligibleStudents()` is the existing gate.

## Data model

### New table `OrgFollowers`

| Column | Type | Notes |
|--------|------|-------|
| `OrgFollowerID` | int PK identity | |
| `OrgID` | int FK → Organizations | cascade delete |
| `UserID` | int FK → Users | cascade delete (so deleting a user isn't blocked by follows) |
| `FollowedAt` | datetime2 | default now |

Unique index on `(OrgID, UserID)`. Unfollow **deletes** the row (follows are
personal choices; no history needed). Add `ICollection<OrgFollower> Followers`
to `Organization` and register the `DbSet` in `ApplicationDbContext`.

### `Organization.Description` becomes About Us

Reuse the existing `Description` column as the About Us text and widen it from
500 to **2000** characters (migration alters the column; no data loss). It is
labelled "About Us" in every view. The admin Edit form keeps editing it too.
*My call:* reusing it avoids two near-identical fields; the hero banner stops
showing it in full (see Views).

### `OrgAnnouncement.PhotoURL`

New nullable `nvarchar(500)` column. `AttachmentURL` stays as-is for files.
Existing posts whose attachment happens to be an image are left alone.

One EF migration covers all three changes.

## Behaviour

### Follow / unfollow

- `POST /Org/Follow/{orgId}` and `POST /Org/Unfollow/{orgId}`, antiforgery
  protected, redirect back to the referring page (org page or `/Org`).
- Follow: allowed only for an eligible student on an active org. Idempotent
  (already following → no-op). Members may also follow; it changes nothing
  for them.
- Unfollow: deletes the row. A member keeps seeing posts via membership, so
  the UI never offers them Unfollow.
- Removed by an admin from membership → loses member access; does **not**
  become a follower automatically.
- Org deactivated → hidden from everyone as today; follow rows are kept and
  come back if it is reactivated.
- Not audited (personal choice, like liking a post).

### About Us editing

- `GET/POST /Org/AboutUs/{orgId}` (or an inline form on the About Us tab, as
  in view C) for officers of that org (`IOrgAccess.IsOfficerAsync`, checked
  at request time) and Administrators.
- Edits only `Description`; max 2000 chars, plain text, line breaks kept
  (`white-space:pre-line`), no HTML.
- Audited: `_audit.Record("Edit About Us", AuditArea.Organizations, orgId, ...)`
  with old/new text, saved in the same `SaveChangesAsync`.

### Posting with a photo

- `OrgPostViewModel` gains `IFormFile? Photo`.
- Photo rules match the org logo: `.jpg/.jpeg/.png/.webp` and the matching
  content types; reuse the existing logo validation helper. Size limit: same
  as the logo's if it has one, otherwise 5 MB (*my call*).
- Upload through `IBlobStorageService` to the existing `org-attachments`
  container.
- The file-attachment field stays, relabelled "File attachment", and stops
  accepting images (`.jpg/.jpeg/.png/.gif` removed from its allow-list) so
  pictures go through the Photo field, as mockup E shows (*my call*; GIFs
  are no longer accepted anywhere).

### Notifications

On a new post, notify the union of active members and followers who are
still eligible students, de-duplicated, excluding the poster. Same type
(`OrgAnnouncement`), message and link (`/Org/Details/{orgId}`) as today.

## Views (see mockups)

- **`/Org/Details/{id}`**
  - Hero: logo, name, college chip, follower count, and the Follow /
    Following / Member button (or New Post for officers).
  - Non-viewers (view A): About Us card, Officers card, locked box ("Announcements
    are for followers") with a second Follow button for students.
  - Viewers (views B/C): tabs **Announcements (n)** | **About Us**,
    Announcements selected by default. Posts render the photo inline (full
    card width, `object-fit:cover`, rounded) above the attachment button.
  - Officers see an Edit control on the About Us tab (view C).
- **`/Org` (view D, as approved)** becomes an org directory, not a post feed.
  - "Orgs you follow" section: one compact row per followed or member org
    (logo, name, "New: <latest post title>" when it has a live post, a
    Following/Member tag). The row links to the org page, where the posts are.
  - "Discover organizations" section: one row per other active org with
    college, follower count, Follow button (students only), linking to its
    page.
  - Non-students (Faculty, Staff, etc.) see one "Organizations" list of rows
    with no Follow buttons. Admins see one list with every org's latest
    title.
  - The org filter dropdown is removed (*my call*: with no post feed there is
    nothing for it to filter; the list is short).
- **Post form (view E)**: separate Photo field with preview and Remove, then
  File attachment.
- Bootstrap 5.1 and the existing `ec-*` classes only (no 5.2+ variable APIs).

## Testing

Extend `EduConnect.Tests` alongside `OrgOfficerTests`:

- Non-follower student gets no posts on Details and `/Org`; follower and
  member do; admin does; faculty does not.
- Follow is refused for non-students, unverified students, inactive orgs;
  follow is idempotent; unfollow removes the row.
- Following does not create an `OrgMembers` row.
- Officer can edit About Us for their own org only; edit is audited;
  a non-officer member and a follower cannot.
- Post notification goes to members ∪ followers, once each, not the poster.
- Photo upload rejects non-image types.

Browser check on port 5120 with `uitest.student` (officer of org 3) and a
second student who is not a member. Don't click actions that email real users.

## Left to the builder

- Exact route names and whether About Us edit is a separate page or inline.
- How the tabs are implemented (Bootstrap 5.1 nav-tabs is fine).
- Ordering inside "Discover organizations" (alphabetical is fine).
- Whether to cache follower counts (counting per request is fine at this scale).
