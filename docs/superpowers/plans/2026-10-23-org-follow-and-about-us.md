# Org Follow, About Us, and Post Photos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Students follow orgs themselves to see their announcements; non-followers see the org's About Us; officers edit their org's About Us; org posts get one inline photo.

**Architecture:** A new `OrgFollowers` table sits beside the admin-managed `OrgMembers`. One access rule (`IOrgAccess.CanSeePostsAsync` / `VisibleOrgIds`) decides post visibility and is used by the org page, the `/Org` directory and post notifications. `Organization.Description` is widened and becomes the About Us text; `OrgAnnouncement` gains `PhotoURL`.

**Tech Stack:** ASP.NET Core MVC (.NET), EF Core + SQL Server (SQLite in tests), xUnit, Razor + Bootstrap 5.1.0, Azure Blob via `IBlobStorageService`.

**Spec:** `docs/superpowers/specs/2026-10-23-org-follow-and-about-us-design.md` (read it first; mockups at `.superpowers/brainstorm/org-follow/org-page-v1.html`).

## Global Constraints

- Work on branch `feature/org-follow` (already cut from `feature/org-officers`, spec committed as `79a118e`). **Never push `main`** — the owner merges and pushes (main deploys to Azure).
- Follow is separate from membership: following never creates or changes an `OrgMembers` row.
- Only eligible students (`IOrgAccess.EligibleStudents()`: active, Student role, Verified) can follow, any college.
- Can see posts for org X = Administrator, OR active non-`Adviser` `OrgMembers` row for X on an active user, OR follow row for X while still an eligible student.
- About Us = `Organization.Description`, max **2000** chars, plain text, rendered with `white-space:pre-line`.
- One photo per post; allowed `.jpg/.jpeg/.png/.webp` with content types `image/jpeg`, `image/png`, `image/webp`, max 5 MB (same rule as the org logo).
- File attachment allow-list becomes `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .zip` (images removed).
- Uploads keep public blob URLs in container `org-attachments`.
- No auto-follow on deploy; students who follow nothing see the hint: `Follow the organizations you care about to see their announcements.`
- Follow/unfollow is not audited. About Us edits are audited with action `Edit About Us`, area `AuditArea.Organizations`.
- Bootstrap 5.1 only (no `--bs-btn-*`, `--bs-link-*`, `text-bg-*`); use existing `ec-*` classes.
- Stop the dev server before `dotnet build` (exe lock). Browser checks: launch config `educonnect-web`, port 5120.

## Review Focus

1. **Member clicks Unfollow / follows again** — a member keeps seeing posts; Unfollow on a member with no follow row is a harmless no-op. → test in Task 4.
2. **Follower later deactivated, unverified, or changed to another role** — stops seeing posts and stops getting notifications, though their follow row remains. → tests in Tasks 3 and 8.
3. **Follow on a deactivated or nonexistent org id** — refused, no row written. → test in Task 4.
4. **Double-submitted Follow** (two tabs, double click) — exactly one row. → test in Task 4.
5. **About Us over 2000 chars or containing HTML** — over-length is rejected with a model error; HTML is stored as text and Razor encodes it on display. → test in Task 7.

---

### Task 1: Bring `main` into the feature branch

**Files:**
- Modify (conflict resolution): `src/EduConnect.Web/Migrations/ApplicationDbContextModelSnapshot.cs`, `src/EduConnect.Web/Data/ApplicationDbContext.cs`, `src/CLAUDE.md`, `src/EduConnect.Web/Controllers/AdminController.cs` (whichever conflict)

**Interfaces:** none.

- [ ] **Step 1: Merge**

Run: `git merge main` on `feature/org-follow`.
Expected: conflicts at most in the model snapshot, `ApplicationDbContext.cs` and `src/CLAUDE.md`. Keep both sides in each (main adds event audience + group expiry; the branch adds the Adviser filtered unique index and officer config).

- [ ] **Step 2: Verify the model still matches the migrations**

Run: `dotnet ef migrations has-pending-model-changes --project src/EduConnect.Web`
Expected: no pending changes. If it reports changes, the snapshot merge dropped something — fix the snapshot, not by adding a migration.

- [ ] **Step 3: Run the whole suite**

Run: `dotnet test src/EduConnect.Tests`
Expected: all pass (includes `OrgOfficerTests`, `EventAudienceTests`, `GroupExpiryTests`).

- [ ] **Step 4: Commit the merge**

```bash
git commit -m "Merge main into org follow branch"
```

---

### Task 2: Schema — followers, longer About Us, post photo

**Files:**
- Create: `src/EduConnect.Web/Models/OrgFollower.cs`
- Modify: `src/EduConnect.Web/Models/Organization.cs`, `src/EduConnect.Web/Models/OrgAnnouncement.cs`, `src/EduConnect.Web/Data/ApplicationDbContext.cs`, `src/EduConnect.Web/ViewModel/OrgViewModel.cs` (`OrgFormViewModel.Description` → `[MaxLength(2000)]`), `Views/Org/Create.cshtml` and `Views/Org/Edit.cshtml` (label the field "About Us", any `maxlength` → 2000)
- Create: migration `AddOrgFollowersAndPostPhotos` (generated)
- Create: `src/EduConnect.Tests/OrgTestBase.cs` (shared fixture), `src/EduConnect.Tests/OrgFollowTests.cs`
- Modify: `src/EduConnect.Tests/OrgOfficerTests.cs` (inherit `OrgTestBase`, delete the moved helpers)

**Interfaces:**
- Produces: `class OrgFollower { int OrgFollowerID; int OrgID; int UserID; DateTime FollowedAt = DateTime.Now; Organization Organization; User User; }` mapped to table `OrgFollowers`; `DbSet<OrgFollower> OrgFollowers`; `Organization.Followers` (`ICollection<OrgFollower>`); `Organization.Description` `[MaxLength(2000)]`; `OrgAnnouncement.PhotoURL` (`string?`, `[MaxLength(500)]`).
- Produces (tests): `abstract class OrgTestBase : IDisposable` with the fields and helpers now in `OrgOfficerTests` (`_db`, `_session`, `_notifications`, `_admin`, `_faculty`, `_bsit`, `_org`, `_otherOrg`, `AddOrg`, `Student`, `Join`, `Controller`, `AsAdmin`, `APost`) plus `OrgFollower Follow(Organization org, User user)` and `static IFormFile Upload(string fileName, string contentType, int bytes = 10)`.

- [ ] **Step 1: Extract `OrgTestBase`** from `OrgOfficerTests` (pure move, make members `protected`). Run `dotnet test src/EduConnect.Tests --filter FullyQualifiedName~OrgOfficerTests` — expected PASS, unchanged count.

- [ ] **Step 2: Write the failing tests** in `OrgFollowTests : OrgTestBase`

```csharp
[Fact]
public void A_student_can_follow_an_org_only_once()
{
    var s = Student();
    Follow(_org, s);
    Assert.ThrowsAny<DbUpdateException>(() => Follow(_org, s));
}

[Fact]
public void Deleting_a_user_removes_their_follows()
{
    var s = Student();
    Follow(_org, s);
    _db.Context.Users.Remove(s);
    _db.Context.SaveChanges();
    Assert.Empty(_db.NewContext().OrgFollowers);
}
```

- [ ] **Step 3: Run to verify they fail** — `dotnet test src/EduConnect.Tests --filter FullyQualifiedName~OrgFollowTests`. Expected: compile failure (`OrgFollowers` not defined).

- [ ] **Step 4: Add the model, DbSet and config.** In `OnModelCreating`: unique index on `(OrgID, UserID)`, both FKs `OnDelete(DeleteBehavior.Cascade)`. Widen `Description`, add `PhotoURL`.

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddOrgFollowersAndPostPhotos --project src/EduConnect.Web`
Expected: creates `OrgFollowers`, alters `Organizations.Description` to `nvarchar(2000)`, adds `OrgAnnouncements.PhotoURL`. If SQL Server rejects two cascade paths, change only the `UserID` FK to `ClientCascade` and keep the test passing.

- [ ] **Step 6: Run tests** — `OrgFollowTests` and `OrgOfficerTests` PASS.

- [ ] **Step 7: Apply locally** — `dotnet ef database update --project src/EduConnect.Web`. Expected: success.

- [ ] **Step 8: Commit** — `git commit -m "Add org followers, longer About Us and post photo column"`

---

### Task 3: Who can see posts

**Files:**
- Modify: `src/EduConnect.Web/Services/IOrgAccess.cs`, `src/EduConnect.Web/Services/OrgAccess.cs`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs`

**Interfaces:**
- Produces on `IOrgAccess`:
  - `Task<bool> CanSeePostsAsync(int userId, string? roleName, int orgId)` — the Global Constraints rule; false for inactive orgs.
  - `IQueryable<int> VisibleOrgIds(int userId, string? roleName)` — active org ids the user may see posts for (all active orgs for Administrator).
  - `IQueryable<int> PostAudience(int orgId)` — user ids to notify: active members on active users ∪ followers who are eligible students; distinct.
  - `Task<bool> IsMemberAsync(int userId, int orgId)` — active non-Adviser row on an active user.

- [ ] **Step 1: Write failing tests** (call `new OrgAccess(_db.Context)` directly):
  - `Follower_member_and_admin_see_posts_others_do_not` — follower ✓, member ✓, officer ✓, admin ✓, non-following student ✗, faculty ✗.
  - `Follower_who_is_deactivated_or_unverified_loses_access` — follow row present, `IsActive=false` → ✗; `VerificationStatus=Pending` → ✗. *(Review Focus 2)*
  - `Retired_adviser_row_grants_nothing` — inactive `Adviser` row → ✗.
  - `Nobody_sees_posts_of_a_deactivated_org` — admin ✗ for `IsActive=false` org.
  - `VisibleOrgIds_lists_followed_and_member_orgs_only` — follows `_org`, member of `_otherOrg`, a third org absent; admin gets all active.
  - `PostAudience_is_members_and_followers_once_each` — member who also follows appears once; inactive member and unverified follower excluded.

- [ ] **Step 2: Run to verify they fail** (compile errors).
- [ ] **Step 3: Implement** in `OrgAccess` reusing `EligibleStudents()`; keep queries translatable (no client evaluation).
- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "Decide org post visibility from membership and follows"`

---

### Task 4: Follow and Unfollow actions

**Files:**
- Modify: `src/EduConnect.Web/Controllers/OrgController.cs`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs`

**Interfaces:**
- Consumes: `IOrgAccess.EligibleStudents()`.
- Produces: `[HttpPost("Org/Follow/{orgId:int}")] [ValidateAntiForgeryToken] Task<IActionResult> Follow(int orgId, string? returnUrl)` and `[HttpPost("Org/Unfollow/{orgId:int}")] ... Unfollow(int orgId, string? returnUrl)`. Both redirect to `returnUrl` when `Url.IsLocalUrl(returnUrl)`, else to `Details`. Refusals set `TempData["Error"]`.

- [ ] **Step 1: Write failing tests:**
  - `Student_follows_and_unfollows` — row created then removed; no `OrgMembers` row ever created.
  - `Following_twice_keeps_one_row` *(Review Focus 4)*.
  - `Faculty_and_unverified_students_cannot_follow` — no row, `TempData["Error"]` set.
  - `Cannot_follow_a_deactivated_or_missing_org` *(Review Focus 3)* — deactivated → no row; id 9999 → `NotFoundResult`.
  - `Member_unfollow_is_a_noop_and_keeps_access` *(Review Focus 1)* — member with no follow row: Unfollow redirects, `CanSeePostsAsync` still true.
  - `Students_from_any_college_can_follow` — student in another college follows a CCIT org.
  - `Not_logged_in_is_sent_to_login`.

  Tests need `controller.Url = new FakeUrlHelper()` from `ControllerFakes.cs`.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** both actions (not audited).
- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "Let students follow and unfollow organizations"`

---

### Task 5: Org page — About Us for non-viewers, tabs for viewers

**Files:**
- Modify: `OrgController.Details`, `src/EduConnect.Web/Views/Org/Details.cshtml`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs`

**Interfaces:**
- Consumes: `CanSeePostsAsync`, `IsMemberAsync`, `IsOfficerOf`.
- Produces ViewBag keys: `CanSeePosts` (bool), `Relation` (string: `"Member"`, `"Following"`, `"None"`, or `"NotStudent"` for roles that cannot follow), `FollowerCount` (int, follow rows of eligible students), `IsOfficer`, `Officers`, `Announcements` (empty list when `CanSeePosts` is false — never query posts for them).

- [ ] **Step 1: Write failing tests:**
  - `Non_follower_gets_about_us_and_no_posts` — `Announcements` empty, `CanSeePosts` false, `Relation == "None"`.
  - `Follower_gets_posts` — `Relation == "Following"`, posts present.
  - `Member_relation_is_member_even_if_also_following`.
  - `Faculty_sees_about_us_without_follow_button` — `Relation == "NotStudent"`, no posts.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement the action**, then the view per mockups A/B/C: hero shows follower count and the button by `Relation` (Follow = gold form post with antiforgery and `returnUrl`; Following = outline form post to Unfollow; Member = disabled; NotStudent = none; officers also see New Post). Non-viewers: About Us card, Officers card, locked box with copy `Announcements are for followers` / `Follow this org to see its posts and get notified when it posts something new.` plus a Follow button for students. Viewers: Bootstrap 5.1 `nav-tabs` "Announcements (n)" (default) | "About Us"; posts show `PhotoURL` as `<img class="img-fluid rounded mt-2" style="max-height:420px;object-fit:cover;width:100%">` above the attachment button. About Us text no longer appears in the hero.
- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "Show About Us to non-followers and posts to followers"`

---

### Task 6: `/Org` becomes an org directory

**Files:**
- Modify: `OrgController.Index`, `src/EduConnect.Web/Views/Org/Index.cshtml`, `src/EduConnect.Web/ViewModel/OrgViewModel.cs`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs`

**Interfaces:**
- Produces: `OrgDirectoryViewModel { List<OrgDirectoryRow> Following; List<OrgDirectoryRow> Discover; bool CanFollow; int? OfficerOrgID; }` and `OrgDirectoryRow { Organization Org; int FollowerCount; string? LatestTitle; string Relation; }` (`Relation` values as in Task 5). `Index()` takes no parameters (the `orgId` filter and `OrgFeedViewModel`/`OrgFeedGroup` are deleted).
- `LatestTitle` is set only for orgs in `VisibleOrgIds`: the newest unexpired post title (pinned first is not required here — newest by `PostedAt`).

- [ ] **Step 1: Write failing tests:**
  - `Directory_splits_followed_and_other_orgs` — student follows `_org`: `Following` = [`_org`], `Discover` = [`_otherOrg`].
  - `Discover_rows_never_leak_post_titles` — `_otherOrg` has a post; its row's `LatestTitle` is null.
  - `Expired_posts_are_not_the_latest_title`.
  - `Non_students_get_one_list_without_follow` — faculty: `CanFollow` false, all orgs in `Discover`, no titles. Admin: all orgs, titles present.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the action and view per mockup D: section headers `ORGS YOU FOLLOW` / `DISCOVER ORGANIZATIONS` (non-students: single `Organizations` list), each row links to `/Org/Details/{id}`; following rows show `New: {LatestTitle}` when present; discover rows show college (or `University-wide`) and `{n} followers` plus a Follow form post (students only, `returnUrl=/Org`). Students with an empty `Following` see the hint from Global Constraints above the Discover list. Keep the officer's New Post button.
- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "Turn the organizations page into a follow directory"`

---

### Task 7: Officers edit About Us

**Files:**
- Modify: `OrgController.cs`, `OrgViewModel.cs`, `Views/Org/Details.cshtml` (Edit link on the About Us tab)
- Create: `src/EduConnect.Web/Views/Org/AboutUs.cshtml`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs`

**Interfaces:**
- Produces: `OrgAboutUsViewModel { int OrgID; string OrgName; [MaxLength(2000, ErrorMessage = "About Us can be at most 2000 characters.")] string? AboutUs; }`; `[HttpGet("Org/AboutUs/{orgId:int}")] AboutUs(int orgId)` and `[HttpPost(...)] [ValidateAntiForgeryToken] AboutUs(int orgId, OrgAboutUsViewModel vm)`. Allowed: `IsOfficerOf(orgId)` or Administrator; others redirect to `Details`. Success redirects to `Details` with `TempData["Success"] = "About Us updated."`.

- [ ] **Step 1: Write failing tests:**
  - `Officer_edits_about_us_and_it_is_audited` — `Description` saved; one `AuditLogs` row with `Action == "Edit About Us"`, `TableAffected == AuditArea.Organizations`, old and new text in values.
  - `Admin_can_edit_about_us`.
  - `Member_follower_and_other_org_officer_cannot_edit` — `Description` unchanged, no audit row.
  - `About_us_over_2000_chars_is_rejected` *(Review Focus 5)* — validate the model (`Validator.TryValidateObject` into `ModelState`) with 2001 chars; view returned, nothing saved.
  - `About_us_html_is_stored_as_text` *(Review Focus 5)* — `<script>x</script>` saved verbatim (Razor encodes on output; the view must use `@Model.Description`, never `Html.Raw`).
  - `Officer_edit_cannot_change_name_logo_or_college` — after POST, those fields equal their originals.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** actions and the view (textarea, live `n / 2000` counter, Cancel back to Details) matching mockup C.
- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "Let organization officers edit their About Us"`

---

### Task 8: Post photo, file allow-list, notify followers

**Files:**
- Modify: `OrgController.Post` (POST), `OrgViewModel.cs` (`OrgPostViewModel.Photo`), `src/EduConnect.Web/Views/Org/Post.cshtml`
- Test: `src/EduConnect.Tests/OrgFollowTests.cs` (and update the notification test in `OrgOfficerTests` if its expectation changes)

**Interfaces:**
- Consumes: `IOrgAccess.PostAudience(orgId)`, `ValidateLogo` (rename to `ValidateImage`; same rules and message).
- Produces: `OrgPostViewModel.Photo` (`IFormFile?`), stored in `OrgAnnouncement.PhotoURL` via `IBlobStorageService.UploadAsync(bytes, $"{Guid}{ext}", "org-attachments", contentType)`.

- [ ] **Step 1: Write failing tests:**
  - `Post_with_photo_stores_photo_url` — `Upload("poster.png","image/png")`; `PhotoURL` non-null, `AttachmentURL` null.
  - `Non_image_photo_is_rejected` — `Upload("x.pdf","application/pdf")` → view returned, `ModelState["Photo"]` has an error, no post.
  - `Image_as_file_attachment_is_rejected` — `Attachment = Upload("p.jpg","image/jpeg")` → error on `Attachment`, no post.
  - `New_post_notifies_members_and_followers_once` — member, follower, member-who-follows, poster, unverified follower: notified = member, follower, member-who-follows (once each).
  - `Follower_who_left_the_student_role_is_not_notified` *(Review Focus 2)*.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement**: validate photo with `ValidateImage`, drop `.jpg .jpeg .png .gif` from the attachment allow-list and change its message to `File type not allowed. Accepted: PDF, Word, Excel, PowerPoint, text, ZIP.`, notify `PostAudience(orgId)` minus the poster. In `Post.cshtml` add the Photo input (`accept=".jpg,.jpeg,.png,.webp"`, preview + Remove) above the relabelled "File attachment" input, per mockup E.
- [ ] **Step 4: Run tests** — whole `OrgFollowTests` and `OrgOfficerTests` PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add a photo to org posts and notify followers"`

---

### Task 9: Verify end to end

- [ ] **Step 1: Full suite** — `dotnet test src/EduConnect.Tests`. Expected: all pass.
- [ ] **Step 2: Browser check** (preview `educonnect-web`, port 5120; see memory for logins): as `uitest.student` (officer of org 3) confirm view C, New Post with a photo, About Us edit. As a second, non-member verified student: org page shows view A with no posts; `/Org` shows the hint and Discover rows without titles; Follow → view B with posts; Unfollow → view A. As Faculty: About Us view, no Follow button. Phone width (375px): hero buttons wrap, no horizontal scroll. Do not trigger actions that email real users.
- [ ] **Step 3: Note for the owner's deploy** — Azure needs migrations `RetireOrgAdvisers` and `AddOrgFollowersAndPostPhotos`; students will see no org posts until they follow (decision #8).
