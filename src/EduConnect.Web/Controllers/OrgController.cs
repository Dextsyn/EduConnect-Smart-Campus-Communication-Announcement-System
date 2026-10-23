using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class OrgController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrgController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IOrgAccess _orgAccess;
        private readonly IAuditService _audit;
        private readonly INotificationService _notifications;
        private readonly IPlacementService _placement;

        public OrgController(
            ApplicationDbContext context,
            ILogger<OrgController> logger,
            IWebHostEnvironment environment,
            IBlobStorageService blobStorageService,
            IOrgAccess orgAccess,
            IAuditService audit,
            INotificationService notifications,
            IPlacementService placement)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
            _blobStorageService = blobStorageService;
            _orgAccess = orgAccess;
            _audit = audit;
            _notifications = notifications;
            _placement = placement;
        }

        // ─── Helpers ───────────────────────────
        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private string? GetRoleName() =>
            HttpContext.Session.GetString("RoleName");

        private bool IsAdmin() =>
            HttpContext.Session.GetString("RoleName") == RoleNames.Administrator;

        // Only an organization's officers post for it — no review step.
        // Checked against the database, not the session's role, so a
        // student who is deactivated or removed loses it immediately.
        private Task<bool> IsOfficerOf(int orgId) =>
            _orgAccess.IsOfficerAsync(GetUserID(), orgId);

        // ─── GET: /Org ─────────────────────────
        public async Task<IActionResult> Index()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userId = GetUserID();
            var now = DateTime.Now;

            var orgs = await _context.Organizations
                .Include(o => o.College)
                .Where(o => o.IsActive)
                .OrderBy(o => o.OrgName)
                .ToListAsync();

            var visible = (await _orgAccess.VisibleOrgIds(userId, GetRoleName()).ToListAsync())
                .ToHashSet();
            var latestTitles = (await _context.OrgAnnouncements
                    .Where(a => visible.Contains(a.OrgID) &&
                                (a.ExpiresAt == null || a.ExpiresAt > now))
                    .Select(a => new { a.OrgID, a.Title, a.PostedAt })
                    .ToListAsync())
                .GroupBy(a => a.OrgID)
                .ToDictionary(g => g.Key, g => g.MaxBy(a => a.PostedAt)!.Title);

            var canFollow = await _orgAccess.EligibleStudents().AnyAsync(u => u.UserID == userId);
            var memberOf = (await _context.OrgMembers
                    .Where(m => m.UserID == userId && m.IsActive &&
                                m.OrgRole != OrgRoles.Adviser && m.User.IsActive)
                    .Select(m => m.OrgID)
                    .ToListAsync())
                .ToHashSet();
            var follows = (await _context.OrgFollowers
                    .Where(f => f.UserID == userId)
                    .Select(f => f.OrgID)
                    .ToListAsync())
                .ToHashSet();
            var followerCounts = await FollowerCountsAsync();

            var rows = orgs.Select(o => new OrgDirectoryRow
            {
                Org = o,
                FollowerCount = followerCounts.GetValueOrDefault(o.OrgID),
                LatestTitle = latestTitles.GetValueOrDefault(o.OrgID),
                Relation = memberOf.Contains(o.OrgID) ? OrgRelation.Member
                    : !canFollow ? OrgRelation.NotStudent
                    : follows.Contains(o.OrgID) ? OrgRelation.Following
                    : OrgRelation.None
            }).ToList();

            bool mine(OrgDirectoryRow r) =>
                r.Relation == OrgRelation.Member || r.Relation == OrgRelation.Following;

            var officerOrg = await _orgAccess.OfficerOrgAsync(userId);
            var vm = new OrgDirectoryViewModel
            {
                Following = rows.Where(mine).ToList(),
                Discover = rows.Where(r => !mine(r)).ToList(),
                CanFollow = canFollow,
                OfficerOrgID = officerOrg != null && await IsOfficerOf(officerOrg.OrgID)
                    ? officerOrg.OrgID : null
            };

            return View(vm);
        }

        // ─── GET: /Org/Details/{id} ────────────
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .Include(o => o.College)
                .FirstOrDefaultAsync(o => o.OrgID == id && o.IsActive);

            if (org == null) return NotFound();

            var userId = GetUserID();
            var canSeePosts = await _orgAccess.CanSeePostsAsync(userId, GetRoleName(), id);

            // Everyone else gets the About Us view; their posts are never loaded.
            var now = DateTime.Now;
            var posts = !canSeePosts ? new List<OrgAnnouncement>() : await _context.OrgAnnouncements
                .Include(a => a.PostedBy)
                .Where(a => a.OrgID == id &&
                            (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.IsPinned)
                .ThenByDescending(a => a.PostedAt)
                .ToListAsync();

            var followerCounts = await FollowerCountsAsync();
            ViewBag.CanSeePosts = canSeePosts;
            ViewBag.Relation = await RelationToAsync(userId, id);
            ViewBag.FollowerCount = followerCounts.GetValueOrDefault(id);
            ViewBag.IsOfficer = await IsOfficerOf(id);
            ViewBag.Officers = await _context.OrgMembers
                .Where(m => m.OrgID == id &&
                            m.OrgRole == OrgRoles.Officer &&
                            m.IsActive &&
                            _orgAccess.EligibleStudents().Any(u => u.UserID == m.UserID))
                .OrderBy(m => m.User.LastName)
                .ThenBy(m => m.User.FirstName)
                .Select(m => new OrgMemberRow
                {
                    Name = m.User.FirstName + " " + m.User.LastName,
                    Position = m.Position
                })
                .ToListAsync();
            ViewBag.Announcements = posts;
            return View(org);
        }

        // ─── POST: /Org/Follow/{orgId} ─────────
        // Any eligible student may follow any active org, whatever their
        // college. Following is not membership and is not audited.
        [HttpPost("Org/Follow/{orgId:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Follow(int orgId, string? returnUrl)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!await _context.Organizations.AnyAsync(o => o.OrgID == orgId && o.IsActive))
                return NotFound();

            var userId = GetUserID();
            if (!await _orgAccess.EligibleStudents().AnyAsync(u => u.UserID == userId))
            {
                TempData["Error"] = "Only verified students can follow organizations.";
                return BackTo(orgId, returnUrl);
            }

            if (!await _context.OrgFollowers.AnyAsync(f => f.OrgID == orgId && f.UserID == userId))
            {
                _context.OrgFollowers.Add(new OrgFollower { OrgID = orgId, UserID = userId });
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // A second click raced this one; the unique index kept
                    // one row, which is all following needs.
                }
            }

            return BackTo(orgId, returnUrl);
        }

        // ─── POST: /Org/Unfollow/{orgId} ───────
        // Members keep seeing posts through their membership, so this only
        // removes a follow row when there is one.
        [HttpPost("Org/Unfollow/{orgId:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unfollow(int orgId, string? returnUrl)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userId = GetUserID();
            var row = await _context.OrgFollowers
                .FirstOrDefaultAsync(f => f.OrgID == orgId && f.UserID == userId);
            if (row != null)
            {
                _context.OrgFollowers.Remove(row);
                await _context.SaveChangesAsync();
            }

            return BackTo(orgId, returnUrl);
        }

        // How the viewer stands with an org; drives the Follow button.
        private async Task<string> RelationToAsync(int userId, int orgId)
        {
            if (await _orgAccess.IsMemberAsync(userId, orgId))
                return OrgRelation.Member;
            if (!await _orgAccess.EligibleStudents().AnyAsync(u => u.UserID == userId))
                return OrgRelation.NotStudent;
            return await _context.OrgFollowers.AnyAsync(f => f.OrgID == orgId && f.UserID == userId)
                ? OrgRelation.Following
                : OrgRelation.None;
        }

        // Followers per org, counting only those who are still eligible
        // students.
        private Task<Dictionary<int, int>> FollowerCountsAsync() =>
            _context.OrgFollowers
                .Where(f => _orgAccess.EligibleStudents().Any(u => u.UserID == f.UserID))
                .GroupBy(f => f.OrgID)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

        private IActionResult BackTo(int orgId, string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Details", new { id = orgId });

        // ─── GET: /Org/Post/{orgId} ────────────
        [HttpGet("Org/Post/{orgId:int}")]
        public async Task<IActionResult> Post(int orgId)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!await IsOfficerOf(orgId))
                return RedirectToAction("Index");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == orgId && o.IsActive);

            if (org == null) return NotFound();

            return View(new OrgPostViewModel
            {
                OrgID = orgId,
                OrgName = org.OrgName
            });
        }

        // ─── POST: /Org/Post/{orgId} ───────────
        [HttpPost("Org/Post/{orgId:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int orgId, OrgPostViewModel vm)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!await IsOfficerOf(orgId))
                return RedirectToAction("Index");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == orgId && o.IsActive);

            if (org == null) return NotFound();

            vm.OrgID = orgId;
            vm.OrgName = org.OrgName;

            if (!ModelState.IsValid)
                return View(vm);

            string? attachmentUrl = null;
            if (vm.Attachment != null && vm.Attachment.Length > 0)
            {
                var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                    ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".zip" };
                var ext = Path.GetExtension(vm.Attachment.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError("Attachment",
                        "File type not allowed. Accepted: PDF, Word, Excel, PowerPoint, images, ZIP.");
                    return View(vm);
                }

                byte[] attachmentBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await vm.Attachment.CopyToAsync(memoryStream);
                    attachmentBytes = memoryStream.ToArray();
                }
                var fileName = $"{Guid.NewGuid()}{ext}";
                attachmentUrl = await _blobStorageService.UploadAsync(
                    attachmentBytes, fileName, "org-attachments", vm.Attachment.ContentType);
            }

            var posterId = GetUserID();
            _context.OrgAnnouncements.Add(new OrgAnnouncement
            {
                OrgID = orgId,
                PostedByID = posterId,
                Title = vm.Title,
                Body = vm.Body,
                AttachmentURL = attachmentUrl,
                IsPinned = vm.IsPinned,
                ExpiresAt = vm.ExpiresAt
            });
            await _context.SaveChangesAsync();

            // Anyone can read the org page; only its members are notified.
            var memberIds = await _context.OrgMembers
                .Where(m => m.OrgID == orgId && m.IsActive &&
                            m.User.IsActive && m.UserID != posterId)
                .Select(m => m.UserID)
                .ToListAsync();
            if (memberIds.Count > 0)
            {
                var message = $"{org.OrgName}: {vm.Title}";
                await _notifications.SendToManyAsync(
                    memberIds, "OrgAnnouncement",
                    message.Length > 500 ? message[..500] : message,
                    $"/Org/Details/{orgId}");
            }

            TempData["Success"] = "Post published successfully.";
            return RedirectToAction("Details", new { id = orgId });
        }

        // ─── GET: /Org/Manage ──────────────────
        public async Task<IActionResult> Manage()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var orgs = await _context.Organizations
                .Include(o => o.College)
                .Include(o => o.Members)
                    .ThenInclude(m => m.User)
                .OrderBy(o => o.OrgName)
                .ToListAsync();

            return View(orgs);
        }

        // ─── GET: /Org/Create ──────────────────
        public async Task<IActionResult> Create()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            return View(await BuildOrgFormViewModel());
        }

        // ─── POST: /Org/Create ─────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrgFormViewModel vm)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                await PopulateOrgFormDropdowns(vm);
                return View(vm);
            }

            var logoError = ValidateLogo(vm.Logo);
            if (logoError != null)
            {
                ModelState.AddModelError("Logo", logoError);
                await PopulateOrgFormDropdowns(vm);
                return View(vm);
            }

            var org = new Organization
            {
                OrgName = vm.OrgName,
                Description = vm.Description,
                LogoURL = await SaveLogoFile(vm.Logo),
                CollegeID = vm.CollegeID,
                CreatedByID = GetUserID()
            };

            _context.Organizations.Add(org);
            await _context.SaveChangesAsync();

            _audit.Record("Create", AuditArea.Organizations, org.OrgID,
                $"Created organization {org.OrgName}",
                newValues: OrgSnapshot(org));
            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"Organization \"{org.OrgName}\" created. Add its members, then assign its officers.";
            return RedirectToAction("Members", new { id = org.OrgID });
        }

        // ─── GET: /Org/Edit/{id} ───────────────
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == id);

            if (org == null) return NotFound();

            var vm = await BuildOrgFormViewModel();
            vm.OrgName = org.OrgName;
            vm.Description = org.Description;
            vm.ExistingLogoURL = org.LogoURL;
            vm.CollegeID = org.CollegeID;

            ViewBag.OrgID = id;
            return View(vm);
        }

        // ─── POST: /Org/Edit/{id} ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, OrgFormViewModel vm)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == id);

            if (org == null) return NotFound();

            if (!ModelState.IsValid)
            {
                await PopulateOrgFormDropdowns(vm);
                vm.ExistingLogoURL = org.LogoURL;
                ViewBag.OrgID = id;
                return View(vm);
            }

            var logoError = ValidateLogo(vm.Logo);
            if (logoError != null)
            {
                ModelState.AddModelError("Logo", logoError);
                await PopulateOrgFormDropdowns(vm);
                vm.ExistingLogoURL = org.LogoURL;
                ViewBag.OrgID = id;
                return View(vm);
            }

            var before = OrgSnapshot(org);

            org.OrgName = vm.OrgName;
            org.Description = vm.Description;
            org.CollegeID = vm.CollegeID;
            org.UpdatedAt = DateTime.Now;

            if (vm.Logo != null && vm.Logo.Length > 0)
            {
                var oldLogoURL = org.LogoURL;
                org.LogoURL = await SaveLogoFile(vm.Logo);
                await DeleteLogoBlobAsync(oldLogoURL);
            }

            _audit.Record("Edit", AuditArea.Organizations, org.OrgID,
                $"Edited organization {org.OrgName}",
                before, OrgSnapshot(org));
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Organization \"{org.OrgName}\" updated.";
            return RedirectToAction("Manage");
        }

        // ─── POST: /Org/Deactivate ─────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound();

            org.IsActive = !org.IsActive;
            org.UpdatedAt = DateTime.Now;
            _audit.Record(org.IsActive ? "Reactivate" : "Deactivate",
                AuditArea.Organizations, org.OrgID,
                $"{(org.IsActive ? "Reactivated" : "Deactivated")} organization {org.OrgName}");
            await _context.SaveChangesAsync();

            TempData["Success"] = org.IsActive
                ? $"\"{org.OrgName}\" reactivated."
                : $"\"{org.OrgName}\" deactivated.";

            return RedirectToAction("Manage");
        }

        // ═══════════════════════════════════════
        //  Members & Officers (Administrator)
        //  Members are added by an admin from all verified students. An
        //  officer is picked from the organization's own members and may
        //  be an officer of one organization only.
        // ═══════════════════════════════════════

        // ─── GET: /Org/Members/{id} ────────────
        public async Task<IActionResult> Members(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .Include(o => o.College)
                .FirstOrDefaultAsync(o => o.OrgID == id);
            if (org == null) return NotFound();

            var eligible = _orgAccess.EligibleStudents();
            var rows = await _context.OrgMembers
                .Where(m => m.OrgID == id && m.IsActive &&
                            m.OrgRole != OrgRoles.Adviser)
                .Select(m => new OrgMemberRow
                {
                    MemberID = m.MemberID,
                    UserID = m.UserID,
                    Name = m.User.FirstName + " " + m.User.LastName +
                           (m.User.Suffix == null ? "" : " " + m.User.Suffix),
                    StudentID = m.User.StudentID ?? "—",
                    Email = m.User.Email,
                    Position = m.Position,
                    IsOfficer = m.OrgRole == OrgRoles.Officer,
                    IsEligible = eligible.Any(u => u.UserID == m.UserID),
                    JoinedAt = m.JoinedAt
                })
                .ToListAsync();

            var placements = await _placement.GetPlacementLabelsAsync(rows.Select(r => r.UserID));
            foreach (var r in rows)
                r.Placement = placements.GetValueOrDefault(r.UserID, "—");

            rows = rows.OrderBy(r => r.Name).ToList();
            return View(new OrgMembersViewModel
            {
                Org = org,
                Officers = rows.Where(r => r.IsOfficer).ToList(),
                Members = rows
            });
        }

        // ─── GET: /Org/SearchStudents?orgId=1&q=juan ──
        // Verified students who are not yet members of this organization.
        public async Task<IActionResult> SearchStudents(int orgId, string? q)
        {
            if (!IsAdmin())
                return Json(new { error = "forbidden" });

            var words = SearchWords(q);
            if (words == null)
                return Json(new { results = Array.Empty<object>() });

            var candidates = _orgAccess.EligibleStudents()
                .Where(u => !_context.OrgMembers.Any(m =>
                    m.OrgID == orgId && m.UserID == u.UserID && m.IsActive));

            // A student number narrows in the database; names are matched
            // here so accent folding is the same on every database.
            if (words.Length == 1 && words[0].All(char.IsDigit))
                candidates = candidates.Where(u => u.StudentID != null &&
                                                   u.StudentID.StartsWith(words[0]));

            var people = await candidates
                .Select(u => new SearchPerson(u.UserID, u.FirstName, u.LastName, u.Suffix, u.StudentID, u.Email))
                .ToListAsync();

            var matches = Match(people, words);
            var placements = await _placement.GetPlacementLabelsAsync(matches.Select(m => m.UserID));

            return Json(new
            {
                results = matches.Select(m => new
                {
                    userID = m.UserID,
                    name = m.Name,
                    studentID = m.StudentID ?? "—",
                    email = m.Email,
                    placement = placements.GetValueOrDefault(m.UserID, "—")
                })
            });
        }

        // ─── GET: /Org/SearchMembers?orgId=1&q=juan ───
        // This organization's members who can hold an officer post.
        public async Task<IActionResult> SearchMembers(int orgId, string? q)
        {
            if (!IsAdmin())
                return Json(new { error = "forbidden" });

            var words = SearchWords(q);
            if (words == null)
                return Json(new { results = Array.Empty<object>() });

            var eligible = _orgAccess.EligibleStudents();
            var members = await _context.OrgMembers
                .Where(m => m.OrgID == orgId && m.IsActive &&
                            m.OrgRole != OrgRoles.Adviser &&
                            eligible.Any(u => u.UserID == m.UserID))
                .Select(m => new
                {
                    m.MemberID,
                    m.OrgRole,
                    Person = new SearchPerson(m.UserID, m.User.FirstName, m.User.LastName,
                        m.User.Suffix, m.User.StudentID, m.User.Email)
                })
                .ToListAsync();

            var matches = Match(members.Select(m => m.Person).ToList(), words);
            var matchedIds = matches.Select(m => m.UserID).ToList();
            var placements = await _placement.GetPlacementLabelsAsync(matchedIds);
            var officerElsewhere = await _context.OrgMembers
                .Where(m => matchedIds.Contains(m.UserID) && m.OrgID != orgId &&
                            m.OrgRole == OrgRoles.Officer && m.IsActive)
                .Select(m => new { m.UserID, m.Organization.OrgName })
                .ToDictionaryAsync(m => m.UserID, m => m.OrgName);
            var byUser = members.ToDictionary(m => m.Person.UserID);

            return Json(new
            {
                results = matches.Select(m => new
                {
                    memberID = byUser[m.UserID].MemberID,
                    name = m.Name,
                    studentID = m.StudentID ?? "—",
                    email = m.Email,
                    placement = placements.GetValueOrDefault(m.UserID, "—"),
                    isOfficer = byUser[m.UserID].OrgRole == OrgRoles.Officer,
                    officerOf = officerElsewhere.GetValueOrDefault(m.UserID)
                })
            });
        }

        // ─── POST: /Org/AddMember ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMember(int orgId, int userId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations.FindAsync(orgId);
            if (org == null) return NotFound();

            var student = await _orgAccess.EligibleStudents()
                .FirstOrDefaultAsync(u => u.UserID == userId);
            if (student == null)
                return MembersError(orgId, "Only active, verified students can be members.");

            var row = await _context.OrgMembers
                .FirstOrDefaultAsync(m => m.OrgID == orgId && m.UserID == userId);
            if (row?.IsActive == true)
                return MembersError(orgId, $"{FullName(student)} is already a member.");

            if (row == null)
            {
                row = new OrgMember { OrgID = orgId, UserID = userId };
                _context.OrgMembers.Add(row);
            }
            row.OrgRole = OrgRoles.Member;
            row.Position = null;
            row.IsActive = true;
            row.JoinedAt = DateTime.Now;

            _audit.Record("Add member", AuditArea.Organizations, orgId,
                $"Added {Describe(student)} to {org.OrgName}",
                newValues: new { OrgID = orgId, UserID = userId, OrgRole = OrgRoles.Member });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{FullName(student)} added as a member.";
            return RedirectToAction("Members", new { id = orgId });
        }

        // ─── POST: /Org/RemoveMember ───────────
        // Ends the membership, and with it any officer post.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int orgId, int memberId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var row = await ActiveMemberAsync(orgId, memberId);
            if (row == null)
                return MembersError(orgId, "That member was not found.");

            var before = MemberSnapshot(row);
            var wasOfficer = row.OrgRole == OrgRoles.Officer;
            row.IsActive = false;
            row.OrgRole = OrgRoles.Member;
            row.Position = null;

            _audit.Record("Remove member", AuditArea.Organizations, orgId,
                $"Removed {Describe(row.User)} from {row.Organization.OrgName}" +
                (wasOfficer ? " (was an officer)" : ""),
                before, MemberSnapshot(row));
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{FullName(row.User)} removed from the organization.";
            return RedirectToAction("Members", new { id = orgId });
        }

        // ─── POST: /Org/AssignOfficer ──────────
        // Also changes a sitting officer's title.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignOfficer(int orgId, int memberId, string? position)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var row = await ActiveMemberAsync(orgId, memberId);
            if (row == null)
                return MembersError(orgId, "Only a member of this organization can be an officer.");

            if (!await _orgAccess.EligibleStudents().AnyAsync(u => u.UserID == row.UserID))
                return MembersError(orgId,
                    $"{FullName(row.User)}'s account is not an active, verified student account.");

            var elsewhere = await _orgAccess.OfficerOrgAsync(row.UserID);
            if (elsewhere != null && elsewhere.OrgID != orgId)
                return MembersError(orgId,
                    $"{FullName(row.User)} is already an officer of {elsewhere.OrgName}. " +
                    "A student can be an officer of one organization only.");

            position = string.IsNullOrWhiteSpace(position) ? null : position.Trim();
            if (position?.Length > 100)
                return MembersError(orgId, "The title can be at most 100 characters.");

            var before = MemberSnapshot(row);
            var wasOfficer = row.OrgRole == OrgRoles.Officer;
            row.OrgRole = OrgRoles.Officer;
            row.Position = position;

            _audit.Record(wasOfficer ? "Edit officer" : "Assign officer",
                AuditArea.Organizations, orgId,
                $"{(wasOfficer ? "Changed the title of" : "Made")} {Describe(row.User)} " +
                $"{(wasOfficer ? "in" : "an officer of")} {row.Organization.OrgName}" +
                (position == null ? "" : $" ({position})"),
                before, MemberSnapshot(row));

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // IX_OrgMembers_OneOfficerOrgPerUser: another admin made
                // them an officer elsewhere a moment ago.
                return MembersError(orgId,
                    $"{FullName(row.User)} is already an officer of another organization.");
            }

            TempData["Success"] = wasOfficer
                ? $"{FullName(row.User)}'s title updated."
                : $"{FullName(row.User)} is now an Organization Officer.";
            return RedirectToAction("Members", new { id = orgId });
        }

        // ─── POST: /Org/UnassignOfficer ────────
        // They stay a member.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnassignOfficer(int orgId, int memberId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var row = await ActiveMemberAsync(orgId, memberId);
            if (row == null || row.OrgRole != OrgRoles.Officer)
                return MembersError(orgId, "That officer was not found.");

            var before = MemberSnapshot(row);
            row.OrgRole = OrgRoles.Member;
            row.Position = null;

            _audit.Record("Unassign officer", AuditArea.Organizations, orgId,
                $"Removed {Describe(row.User)} as an officer of {row.Organization.OrgName}",
                before, MemberSnapshot(row));
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{FullName(row.User)} is no longer an officer.";
            return RedirectToAction("Members", new { id = orgId });
        }

        // ─── Members helpers ───────────────────
        private const int SearchMinLength = 2;
        private const int SearchMaxResults = 10;

        private sealed record SearchPerson(
            int UserID, string FirstName, string LastName, string? Suffix, string? StudentID, string Email)
        {
            public string Name => $"{FirstName} {LastName}{(Suffix == null ? "" : " " + Suffix)}";
        }

        // Null when the query is too short to search.
        private static string[]? SearchWords(string? q)
        {
            var words = PersonInput.FoldForSearch(q)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("", words).Length < SearchMinLength ? null : words;
        }

        // Every word must match part of the name, in any order, ignoring
        // case and accents ("pena" finds "Peña"); one word may instead be
        // the start of a student number. Same rule as the event scanner.
        private static List<SearchPerson> Match(List<SearchPerson> people, string[] words) =>
            people
                .Where(p =>
                {
                    var name = PersonInput.FoldForSearch(p.Name);
                    return words.All(w => name.Contains(w)) ||
                           (words.Length == 1 && p.StudentID != null &&
                            p.StudentID.StartsWith(words[0]));
                })
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.StudentID)
                .Take(SearchMaxResults)
                .ToList();

        private Task<OrgMember?> ActiveMemberAsync(int orgId, int memberId) =>
            _context.OrgMembers
                .Include(m => m.User)
                .Include(m => m.Organization)
                .FirstOrDefaultAsync(m => m.MemberID == memberId && m.OrgID == orgId &&
                                          m.IsActive && m.OrgRole != OrgRoles.Adviser);

        private IActionResult MembersError(int orgId, string message)
        {
            TempData["Error"] = message;
            return RedirectToAction("Members", new { id = orgId });
        }

        private static string FullName(User u) => $"{u.FirstName} {u.LastName}";

        private static string Describe(User u) =>
            u.StudentID == null ? FullName(u) : $"{FullName(u)} ({u.StudentID})";

        private static object OrgSnapshot(Organization o) =>
            new { o.OrgName, o.Description, o.CollegeID, o.LogoURL };

        private static object MemberSnapshot(OrgMember m) =>
            new { m.OrgID, m.UserID, m.OrgRole, m.Position, m.IsActive };

        // ─── Private Helpers ───────────────────
        private async Task<OrgFormViewModel> BuildOrgFormViewModel()
        {
            var vm = new OrgFormViewModel();
            await PopulateOrgFormDropdowns(vm);
            return vm;
        }

        private async Task PopulateOrgFormDropdowns(OrgFormViewModel vm)
        {
            vm.CollegeOptions = (await _context.Colleges
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync())
                .Select(c => new SelectListItem(c.Name, c.CollegeID.ToString()))
                .ToList();
        }

        private static readonly string[] AllowedLogoExtensions =
            { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedLogoContentTypes =
            { "image/jpeg", "image/png", "image/webp" };

        private static string? ValidateLogo(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();

            if (!AllowedLogoExtensions.Contains(extension) ||
                !AllowedLogoContentTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.";

            if (file.Length > 5 * 1024 * 1024)
                return "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.";

            return null;
        }

        private async Task<string?> SaveLogoFile(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();
            var fileName = $"{Guid.NewGuid()}{extension}";

            return await _blobStorageService.UploadAsync(
                fileBytes, fileName, "org-logos", file.ContentType);
        }

        private async Task DeleteLogoBlobAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.Contains("/org-logos/", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var blobName = url.Substring(url.LastIndexOf('/') + 1);
                await _blobStorageService.DeleteAsync(blobName, "org-logos");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to delete org logo blob: {Error}", ex.Message);
            }
        }
    }
}
