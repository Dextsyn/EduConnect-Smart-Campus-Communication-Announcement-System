# College > Department > Program hierarchy and role rework — design

Date: 2026-09-28
Status: approved decisions from the 2026-09-28 audit Q&A; implementation split into five plans (see Roadmap).

## Problem

EduConnect models "departments" as one flat `DepartmentTags` list. The academic rows are really
colleges, and the same table also holds non-academic offices (Registrar, Clinic…) and the
`ALL` (School Wide) tag. Announcement targeting, the approval flow, registration and user
placement all key off that flat list, so the app cannot express "the Chairperson of the
Department of Computer Science" or "only BS Information Technology students".

## Decisions (from the audit Q&A)

| # | Topic | Decision |
|---|---|---|
| 1 | BSEd Major in English | Belongs to Department of Teacher Education. The duplicate "Media and Communication" entry is dropped. |
| 2 | Colleges with no departments (Architecture, Law, Nursing, Pharmacy) | Each gets one **implicit** department (`IsImplicit = 1`, same name as the college) that the UI never shows. Programs always hang off a department. |
| 3 | Role name | `Chair Person` → **`Chairperson`**. |
| 4 | Faculty targeting | Faculty may only target programs of their own assigned department. Applies to every department (IT&IS is the motivating example). |
| 5 | Existing students | Every existing student must be placed in their real program (department and college derive from it). |
| 6 | In-flight approvals at switch-over | Anything `PendingChair` / `PendingDean` when the new flow ships is reset to `Draft` and the author is notified to resubmit. |
| 7 | Multiple roles / departments | One role per user, one placement per user. "Organization Adviser" is removed as a role; an adviser is a Faculty user with `OrgMembers.OrgRole = 'Adviser'`. |
| 8 | Legacy tags CLAS, COED, PE | Deleted. |
| 9 | Non-academic offices and `ALL` | Stay on the existing `DepartmentTags` / `UserDepartments` system, outside the hierarchy. |
| 10 | Escalate to Dean | A Chairperson can require Dean approval both while reviewing a Faculty post and when creating their own announcement. Default: Chairperson approval is final. |
| 11 | Dean / Chairperson feed | Only their own college / department (plus `ALL`). |
| 12 | Organizations / study groups | Remain visible to everyone; their department link is a label only. |
| 13 | Retiring a hierarchy item | Soft retire only (see Retire rules). |
| 14 | Administrator announcements | Admins do not create announcements; the dead admin branches in `AnnouncementController` are removed. |
| 15 | Registration | Program is required at registration. Previously approved students are forced to complete their profile. |
| 16 | Branches | `feature/admin-department-crud` merged into `main` (done 2026-09-28); `feature/organization-adviser` deleted (was `74eb432`). |
| — | Placement | Admin assigns Dean / Chairperson / Faculty placements. Admin can also edit any student's program (the user base is small enough for the admin to place existing students). Students still choose their program at registration and can change it in their profile. |

## Data model

New tables (all soft-retirable: `IsActive`, `RetiredAt`):

- `Colleges` — `CollegeID`, `Name` (unique), `ShortName`, `LegacyTagID` (nullable FK to the
  `DepartmentTags` row this college replaces; used for backfill), timestamps.
- `Departments` — `DepartmentID`, `CollegeID` (FK), `Name` (unique per college), `ShortName`,
  `IsImplicit`, timestamps.
- `Programs` — `ProgramID`, `DepartmentID` (FK), `Name` (unique per department), `ShortName`,
  timestamps. C# class is `AcademicProgram` (a class named `Program` would collide with the
  top-level-statements `Program` in `Program.cs`).

`Users` gains nullable `CollegeID`, `DepartmentID`, `ProgramID`. All three are stored
(denormalized) so feed and routing queries stay single-column filters; one service owns
writing them and keeps them consistent:

| Role | Placement set | Derived |
|---|---|---|
| Dean | `CollegeID` | — |
| Chairperson, Faculty | `DepartmentID` | `CollegeID` |
| Student, Student Pending | `ProgramID` | `DepartmentID`, `CollegeID` |
| Administrator, Staff | none | — |

A new `AnnouncementTargets` table (Plan 4) holds hierarchy targeting: exactly one of
`CollegeID` / `DepartmentID` / `ProgramID` per row (check constraint). `AnnouncementTags`
remains for `ALL` and office tags. Targets are stored at the level chosen so programs added
later to a targeted college/department are included automatically.

## Rules

**Targeting (who may pick what)**
- Dean: whole college, or any departments / programs within it.
- Chairperson: whole department, or any programs within it.
- Faculty: programs within their department only.
- `ALL` / office tags: unchanged — whatever the user holds in `UserDepartments`.
- Enforced server-side on Create and Edit; the picker only renders what is allowed.

**Audience (one shared resolver, Plan 4)** — a user is *inside* a target when their most
specific placement lies within it (e.g. a BSIT student is inside a CCIT college target, the
IT&IS department target and the BSIT program target). Notifications go to active users inside
any target, or to everyone for `ALL`. Feed visibility:
- Students / Faculty / Chairperson: announcements whose targets contain them, any target
  within their own department (staff only), plus `ALL`.
- Dean: any target within their college, plus `ALL`.
- `Details` stays unscoped and Explore keeps showing other colleges' announcements — see the
  "department tag semantics" decision; emergencies stay scoped to their targets.

**Approval**
- Faculty submits → `PendingChair`, routed to **every** active Chairperson of the author's
  department. If the department has none (implicit departments) → `PendingDean` to every
  active Dean of the college. None at all → error, as today.
- Chairperson approves → `Approved`, unless "Escalate to Dean" is ticked → `PendingDean`.
- Chairperson creating their own announcement may tick "Requires Dean approval" → saved as
  `PendingDean` instead of publishing.
- Dean approves → `Approved`. Author publishes approved announcements, as today.
- Reviewers are authorized by the author's `DepartmentID` (Chairperson) / `CollegeID` (Dean).

**Retire**
- Retired items disappear from every picker; existing users, targets and announcements keep
  pointing at them and render "(retired)".
- An item cannot be retired while it has active users placed in it or active children;
  the admin reassigns first. Restore is always allowed.
- Implicit departments are retired/restored only together with their college.

**Registration and profile**
- Registration: college → department → program cascade; program required.
- Student profile: can change program (applies immediately, no re-verification — assumption,
  see Open items).
- A verified Student or Student Pending with no `ProgramID` is redirected to Profile with a
  "choose your program" notice on every page except Profile and Logout.

## Migration and rollout

- All data migrations key on names / `ShortName`, never identity IDs (IDs differ between local
  and Azure), and are idempotent.
- Deleting CLAS / COED / PE relies on the Restrict FKs: if anything references them in a
  database, the migration fails loudly instead of silently reassigning.
- Existing academic users get `CollegeID` backfilled from their primary tag via
  `Colleges.LegacyTagID`; department/program placement is manual, done by the admin for
  staff and existing students alike (the student may also do it from their profile).
- Existing announcements get college-level `AnnouncementTargets` backfilled from their
  academic tags (Plan 4), so nothing loses its audience.
- Pushing `main` deploys to Azure; migrations are applied manually. The user pushes.

## Roadmap

1. **Foundations** — role constants, Chairperson rename, remove Organization Adviser role,
   remove admin announcement dead code, delete CLAS/COED/PE, hierarchy tables + seed,
   user placement columns + college backfill. *(plan: 2026-09-28-hierarchy-foundations.md)*
2. **Admin** — hierarchy CRUD (add/edit/retire/restore), admin user placement for every
   role including students, test project.
3. **Students** — registration cascade, profile program editor, forced profile completion.
4. **Targeting** — `AnnouncementTargets`, audience resolver, picker, feed / notification /
   chatbot / dashboard migration, Dean & Chairperson feed scoping, target backfill.
5. **Approval + cleanup** — department routing, escalation toggles, in-flight reset,
   retire academic `DepartmentTags` from the UI, docs and `database/EduConnectDB.sql`.

Each later plan is written when the previous one is merged, against the code as it then is.

## Open items (assumptions — say if wrong)

- Students changing program apply immediately without admin re-verification.
- Multiple Deans per college / Chairpersons per department are allowed; routing notifies all.
- The orphan local tables `Members` / `Orders` (migrations `AddMembers`, `AddOrders`, in no
  branch) and the orphan `ConvertAdvisersToNewRole` history row are left alone.
