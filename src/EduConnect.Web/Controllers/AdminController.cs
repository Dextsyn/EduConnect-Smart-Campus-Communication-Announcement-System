using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AdminController> _logger;
        private readonly IHierarchyService _hierarchy;
        private readonly IPlacementService _placement;
        private readonly IAuditService _audit;

        public AdminController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<AdminController> logger,
            IHierarchyService hierarchy,
            IPlacementService placement,
            IAuditService audit)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
            _hierarchy = hierarchy;
            _placement = placement;
            _audit = audit;
        }

        // ─── Check if Admin ────────────────────
        private bool IsAdmin() =>
            HttpContext.Session
                .GetString("RoleName") == RoleNames.Administrator;

        private string GetBaseUrl() =>
            $"{Request.Scheme}://{Request.Host}";

        // ─── Audit helpers ─────────────────────
        // "BSIT · ITIS · CCIT" (implicit departments hidden), or "—".
        private async Task<string> PlacementTextAsync(int? collegeId, int? departmentId, int? programId)
        {
            var program = programId == null ? null : await _context.Programs
                .Where(p => p.ProgramID == programId)
                .Select(p => p.ShortName ?? p.Name).FirstOrDefaultAsync();
            var department = departmentId == null ? null : await _context.Departments
                .Where(d => d.DepartmentID == departmentId && !d.IsImplicit)
                .Select(d => d.ShortName ?? d.Name).FirstOrDefaultAsync();
            var college = collegeId == null ? null : await _context.Colleges
                .Where(c => c.CollegeID == collegeId)
                .Select(c => c.ShortName ?? c.Name).FirstOrDefaultAsync();

            var text = string.Join(" · ", new[] { program, department, college }
                .Where(s => !string.IsNullOrEmpty(s)));
            return text.Length > 0 ? text : "—";
        }

        // The fields an EditUser audit row compares.
        private async Task<Dictionary<string, string?>> AuditSnapshotAsync(User user)
        {
            var role = await _context.Roles
                .Where(r => r.RoleID == user.RoleID)
                .Select(r => r.RoleName).FirstOrDefaultAsync();
            // Taken before SaveChanges too: pull in tag rows added since
            // the load and skip ones marked for removal.
            _context.ChangeTracker.DetectChanges();
            var tagIds = user.UserDepartments?
                .Where(ud => _context.Entry(ud).State != EntityState.Deleted)
                .Select(ud => ud.TagID).ToList() ?? new List<int>();
            var schoolWide = await _context.DepartmentTags
                .AnyAsync(t => tagIds.Contains(t.TagID) && t.ShortName == AudienceService.SchoolWide);

            return new Dictionary<string, string?>
            {
                ["Name"] = $"{user.FirstName} {user.LastName}",
                ["Email"] = user.Email,
                ["Student ID"] = user.StudentID,
                ["Role"] = role,
                ["Status"] = user.IsActive ? "Active" : "Inactive",
                ["Placement"] = await PlacementTextAsync(user.CollegeID, user.DepartmentID, user.ProgramID),
                ["School Wide"] = schoolWide ? "Can post" : "No"
            };
        }

        // ─── GET: /Admin ───────────────────────
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            // ─── Stat Cards ────────────────────────
            ViewBag.TotalUsers = await _context.Users
                .Where(u => u.VerificationStatus == "Verified")
                .CountAsync();

            ViewBag.PendingVerifications = await _context.Users
                .Where(u => u.VerificationStatus == "Pending")
                .CountAsync();

            ViewBag.CountFaculty = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Faculty && u.IsActive)
                .CountAsync();

            ViewBag.CountDean = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Dean && u.IsActive)
                .CountAsync();

            ViewBag.CountChairPerson = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Chairperson && u.IsActive)
                .CountAsync();

            ViewBag.CountStaff = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Staff && u.IsActive)
                .CountAsync();

            ViewBag.CountStudent = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Student && u.IsActive)
                .CountAsync();

            // ─── Chart: New Registrations Last 6 Months ──
            var months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-i))
                .Reverse()
                .ToList();

            ViewBag.MonthLabels = months
                .Select(m => m.ToString("MMM yyyy"))
                .ToList();

            ViewBag.MonthlyRegistrations = months
                .Select(m => _context.Users
                    .Count(u =>
                        u.CreatedAt.Month == m.Month &&
                        u.CreatedAt.Year == m.Year))
                .ToList();

            // ─── Chart: Users by Role ───────────────
            var roleData = await _context.Users
                .Where(u => u.IsActive)
                .GroupBy(u => u.Role.RoleName)
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .ToListAsync();

            ViewBag.RoleLabels = roleData.Select(r => r.Role).ToList();
            ViewBag.RoleCount = roleData.Select(r => r.Count).ToList();

            // ─── Recent Pending Verifications ───────
            ViewBag.RecentPendingUsers = await _context.Users
                .Include(u => u.UserDepartments)
                    .ThenInclude(ud => ud.DepartmentTag)
                .Where(u => u.VerificationStatus == "Pending")
                .OrderBy(u => u.CreatedAt)
                .Take(5)
                .ToListAsync();

            // ─── Recently Added Users ───────────────
            ViewBag.RecentlyAddedUsers = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.UserDepartments)
                    .ThenInclude(ud => ud.DepartmentTag)
                .Where(u => u.VerificationStatus == "Verified")
                .OrderByDescending(u => u.VerifiedAt)
                .Take(5)
                .ToListAsync();

            return View();
        }
        // ═══════════════════════════════════════
        //  GET: /Admin/PendingUsers
        //  Show all pending student accounts
        // ═══════════════════════════════════════
        public async Task<IActionResult> PendingUsers()
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var pendingUsers = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.UserDepartments)
                    .ThenInclude(ud => ud.DepartmentTag)
                .Where(u => u.VerificationStatus
                    == "Pending")
                .OrderBy(u => u.CreatedAt)
                .ToListAsync();

            return View(pendingUsers);
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/ApproveUser
        //  Approve a pending student account
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveUser(
            int userID)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var adminID = int.Parse(
                HttpContext.Session
                    .GetString("UserID"));

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.UserID == userID);

            if (user == null)
            {
                TempData["Error"] =
                    "User not found.";
                return RedirectToAction("PendingUsers");
            }

            // Get verified student role
            var studentRole = await _context.Roles
                .FirstOrDefaultAsync(r =>
                    r.RoleName == RoleNames.Student);

            // Update user
            user.VerificationStatus = "Verified";
            user.IsActive = true;
            user.RoleID = studentRole.RoleID;
            user.VerifiedByID = adminID;
            user.VerifiedAt = DateTime.Now;
            user.UpdatedAt = DateTime.Now;

            _audit.Record("Approve", AuditArea.Users, user.UserID,
                $"Approved the registration of {user.FirstName} {user.LastName} ({user.Email}).");
            await _context.SaveChangesAsync();

            // Send approval email
            try
            {
                var emailBody = $@"
                <div style='font-family: Arial, sans-serif;
                            max-width: 600px;
                            margin: 0 auto;'>
                    <div style='background: #0d6efd;
                                padding: 30px;
                                text-align: center;
                                border-radius: 8px 8px 0 0;'>
                        <h1 style='color: white; margin: 0;'>
                            EduConnect
                        </h1>
                    </div>
                    <div style='background: #f8f9fa;
                                padding: 30px;
                                border-radius: 0 0 8px 8px;'>
                        <h2 style='color: #198754;'>
                            ✅ Account Approved!
                        </h2>
                        <p>Hi {user.FirstName},</p>
                        <p>
                            Your EduConnect account has been
                            verified by ITC. You can now
                            login and access the system.
                        </p>
                        <div style='text-align: center;
                                    margin: 30px 0;'>
                            <a href='{GetBaseUrl()}/Account/Login'
                               style='background: #0d6efd;
                                       color: white;
                                       padding: 14px 30px;
                                       text-decoration: none;
                                       border-radius: 6px;
                                       font-weight: bold;'>
                                Login to EduConnect
                            </a>
                        </div>
                        <p style='color: #666;'>
                            Welcome to EduConnect!
                        </p>
                    </div>
                </div>";

                await _emailService.SendEmailAsync(
                    user.Email,
                    $"{user.FirstName} {user.LastName}",
                    "EduConnect — Account Approved!",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Approval email failed: {Error}",
                    ex.Message);
            }

            _logger.LogInformation(
                "User {Email} approved by Admin {AdminID}",
                user.Email, adminID);

            TempData["Success"] =
                $"{user.FirstName} {user.LastName}'s " +
                $"account has been approved.";
            return RedirectToAction("PendingUsers");
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/RejectUser
        //  Reject a pending student account
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectUser(
            int userID, string rejectionReason)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.UserID == userID);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("PendingUsers");
            }

            // Update user
            user.VerificationStatus =
                "Rejected";
            user.IsActive = false;
            user.VerificationRejectionReason =
                rejectionReason;
            user.UpdatedAt = DateTime.Now;

            _audit.Record("Reject", AuditArea.Users, user.UserID,
                $"Rejected the registration of {user.FirstName} {user.LastName} ({user.Email}): {rejectionReason}");
            await _context.SaveChangesAsync();

            // Send rejection email
            try
            {
                var emailBody = $@"
                <div style='font-family: Arial, sans-serif;
                            max-width: 600px;
                            margin: 0 auto;'>
                    <div style='background: #dc3545;
                                padding: 30px;
                                text-align: center;
                                border-radius: 8px 8px 0 0;'>
                        <h1 style='color: white; margin: 0;'>
                            EduConnect
                        </h1>
                    </div>
                    <div style='background: #f8f9fa;
                                padding: 30px;
                                border-radius: 0 0 8px 8px;'>
                        <h2 style='color: #dc3545;'>
                            Account Verification Failed
                        </h2>
                        <p>Hi {user.FirstName},</p>
                        <p>
                            Unfortunately your EduConnect
                            account could not be verified.
                        </p>
                        <div style='background: #fff3cd;
                                    padding: 15px;
                                    border-radius: 6px;
                                    margin: 20px 0;'>
                            <strong>Reason:</strong>
                            <p style='margin: 5px 0 0;'>
                                {rejectionReason}
                            </p>
                        </div>
                        <p>
                            Please contact ITC for
                            further assistance.
                        </p>
                    </div>
                </div>";

                await _emailService.SendEmailAsync(
                    user.Email,
                    $"{user.FirstName} {user.LastName}",
                    "EduConnect — Account Verification",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Rejection email failed: {Error}",
                    ex.Message);
            }

            TempData["Success"] =
                $"{user.FirstName} {user.LastName}'s " +
                $"account has been rejected.";
            return RedirectToAction("PendingUsers");
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/Users
        //  Manage all users
        // ═══════════════════════════════════════
        public async Task<IActionResult> Users(
            string? searchQuery,
            string? filterRole,
            string? filterStatus,
            string? filterPlacement)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var query = _context.Users
                .Include(u => u.Role)
                .Include(u => u.UserDepartments)
                    .ThenInclude(ud => ud.DepartmentTag)
                .Include(u => u.College)
                .Include(u => u.Department)
                .Include(u => u.AcademicProgram)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
                query = query.Where(u =>
                    u.FirstName.Contains(searchQuery) ||
                    u.LastName.Contains(searchQuery) ||
                    u.Email.Contains(searchQuery) ||
                    u.StudentID.Contains(searchQuery));

            if (!string.IsNullOrEmpty(filterRole))
                query = query.Where(u =>
                    u.Role.RoleName == filterRole);

            if (!string.IsNullOrEmpty(filterStatus))
                query = query.Where(u =>
                    u.VerificationStatus == filterStatus);

            if (filterPlacement == "missing")
                query = query.Where(PlacementService.NeedsPlacement);

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            ViewBag.Users = users;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.FilterRole = filterRole;
            ViewBag.FilterStatus = filterStatus;
            ViewBag.Roles = await _context
                .Roles.ToListAsync();
            ViewBag.FilterPlacement = filterPlacement;
            ViewBag.NeedsPlacementCount = await _context.Users
                .Where(PlacementService.NeedsPlacement)
                .CountAsync();

            return View();
        }

        // Dropdown data for the Add/Edit User forms.
        private async Task PopulateUserFormAsync(AdminUserFormViewModel model)
        {
            model.Roles = (await _context.Roles.ToListAsync())
                .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                .ToList();
            model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
        }

        // Gives or takes away the School Wide tag, the permission to post
        // campus-wide announcements. Other tag rows (retired college tags
        // kept for history) are left alone. Mutates only; the caller saves.
        private async Task SetSchoolWideAsync(User user, bool canPost)
        {
            var tagId = await _context.DepartmentTags
                .Where(t => t.ShortName == AudienceService.SchoolWide)
                .Select(t => (int?)t.TagID)
                .FirstOrDefaultAsync();
            if (tagId == null)
                return;

            var existing = user.UserDepartments?.FirstOrDefault(ud => ud.TagID == tagId);
            if (canPost && existing == null)
                _context.UserDepartments.Add(new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = tagId.Value,
                    IsPrimary = false,
                    CreatedAt = DateTime.Now
                });
            else if (!canPost && existing != null)
                _context.UserDepartments.Remove(existing);
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/AddUser
        // ═══════════════════════════════════════
        public async Task<IActionResult> AddUser()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new AdminUserFormViewModel();
            await PopulateUserFormAsync(model);
            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/AddUser
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(AdminUserFormViewModel model)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            // Require password for new users
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError("Password", "Password is required when creating a user.");

            // Check email uniqueness
            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                ModelState.AddModelError("Email", "A user with this email already exists.");

            var roleName = await _context.Roles
                .Where(r => r.RoleID == model.RoleID)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync();
            if (roleName == null)
                ModelState.AddModelError("RoleID", "Choose a valid role.");

            var adminID = int.Parse(HttpContext.Session.GetString("UserID"));

            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password ?? ""),
                StudentID = model.StudentID,
                RoleID = model.RoleID,
                IsActive = model.IsActive,
                VerificationStatus = "Verified",
                VerifiedByID = adminID,
                VerifiedAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };

            if (roleName != null)
            {
                var placement = await _placement.ApplyAsync(
                    user, roleName, model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
            }

            if (!ModelState.IsValid)
            {
                await PopulateUserFormAsync(model);
                return View(model);
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await SetSchoolWideAsync(user, model.CanPostSchoolWide);

            _audit.Record("Create", AuditArea.Users, user.UserID,
                $"Created {roleName} account {user.FirstName} {user.LastName} ({user.Email}).",
                newValues: await AuditSnapshotAsync(user));
            await _context.SaveChangesAsync();

            // Send welcome email (fire-and-forget)
            try
            {
                var emailBody = $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;'>
            <div style='background:#0d6efd;padding:30px;text-align:center;border-radius:8px 8px 0 0;'>
                <h1 style='color:white;margin:0;'>EduConnect</h1>
            </div>
            <div style='background:#f8f9fa;padding:30px;border-radius:0 0 8px 8px;'>
                <h2 style='color:#198754;'>✅ Account Created!</h2>
                <p>Hi {user.FirstName},</p>
                <p>An EduConnect account has been created for you by the administrator.
                   You can log in using your email address.</p>
                <div style='text-align:center;margin:30px 0;'>
                    <a href='{GetBaseUrl()}/Account/Login'
                       style='background:#0d6efd;color:white;padding:14px 30px;
                              text-decoration:none;border-radius:6px;font-weight:bold;'>
                        Login to EduConnect
                    </a>
                </div>
            </div>
        </div>";

                await _emailService.SendEmailAsync(
                    user.Email,
                    $"{user.FirstName} {user.LastName}",
                    "EduConnect — Account Created",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError("Welcome email failed: {Error}", ex.Message);
            }

            TempData["Success"] = $"{user.FirstName} {user.LastName}'s account has been created.";
            return RedirectToAction("Users");
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/EditUser/{id}
        // ═══════════════════════════════════════
        public async Task<IActionResult> EditUser(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .Include(u => u.UserDepartments)
                .FirstOrDefaultAsync(u => u.UserID == id);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Users");
            }

            var model = new AdminUserFormViewModel
            {
                UserID = user.UserID,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                StudentID = user.StudentID,
                RoleID = user.RoleID,
                CanPostSchoolWide = await _context.UserDepartments.AnyAsync(ud =>
                    ud.UserID == user.UserID &&
                    ud.DepartmentTag.ShortName == AudienceService.SchoolWide),
                CollegeID = user.CollegeID,
                DepartmentID = user.DepartmentID,
                ProgramID = user.ProgramID,
                IsActive = user.IsActive
            };
            await PopulateUserFormAsync(model);

            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/EditUser/{id}
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, AdminUserFormViewModel model)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            // Check email uniqueness (excluding this user)
            if (await _context.Users.AnyAsync(u => u.Email == model.Email && u.UserID != id))
                ModelState.AddModelError("Email", "A user with this email already exists.");

            // Password validation only when provided
            if (!string.IsNullOrWhiteSpace(model.Password) && model.Password.Length < 6)
                ModelState.AddModelError("Password", "Password must be at least 6 characters.");

            if (!ModelState.IsValid)
            {
                await PopulateUserFormAsync(model);
                return View(model);
            }

            var user = await _context.Users
                .Include(u => u.UserDepartments)
                .FirstOrDefaultAsync(u => u.UserID == id);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Users");
            }

            var before = await AuditSnapshotAsync(user);

            var roleName = await _context.Roles
                .Where(r => r.RoleID == model.RoleID)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync();
            if (roleName == null)
            {
                ModelState.AddModelError("RoleID", "Choose a valid role.");
            }
            else
            {
                var placement = await _placement.ApplyAsync(
                    user, roleName, model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
            }

            if (!ModelState.IsValid)
            {
                // Nothing has been saved; the tracked user is discarded
                // with this request.
                await PopulateUserFormAsync(model);
                return View(model);
            }

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.Email = model.Email;
            user.StudentID = model.StudentID;
            user.RoleID = model.RoleID;
            user.IsActive = model.IsActive;
            if (model.IsActive && user.VerificationStatus != "Verified")
                user.VerificationStatus = "Verified";
            user.UpdatedAt = DateTime.Now;

            if (!string.IsNullOrWhiteSpace(model.Password))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

            await SetSchoolWideAsync(user, model.CanPostSchoolWide);

            // Only what changed; a password reset is recorded, never its value.
            var after = await AuditSnapshotAsync(user);
            var changed = after.Keys.Where(k => before[k] != after[k]).ToList();
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                changed.Add("Password");
                after["Password"] = "reset";
            }
            if (changed.Count > 0)
                _audit.Record("Update", AuditArea.Users, user.UserID,
                    $"Edited {user.FirstName} {user.LastName}: {string.Join(", ", changed)}.",
                    before.Where(kv => changed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
                    after.Where(kv => changed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

            await _context.SaveChangesAsync();

            TempData["Success"] = $"{user.FirstName} {user.LastName}'s account has been updated.";
            return RedirectToAction("Users");
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/DeleteUser/{id}
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var adminID = int.Parse(HttpContext.Session.GetString("UserID"));
            if (id == adminID)
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToAction("Users");
            }

            var user = await _context.Users
                .Include(u => u.Announcements)
                .Include(u => u.OrganizedEvents)
                .FirstOrDefaultAsync(u => u.UserID == id);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Users");
            }

            // Block deletion if user has content that can't be safely removed
            var hasVerifiedOthers = await _context.Users
                .AnyAsync(u => u.VerifiedByID == id);
            var hasApprovedAnnouncements = await _context.Announcements
                .AnyAsync(a => a.ApprovedByID == id || a.ChairApprovedByID == id);
            var hasCreatedStudyGroups = await _context.StudyGroups
                .AnyAsync(g => g.CreatedByID == id);
            var hasIncidentReports = await _context.IncidentReports
                .AnyAsync(r => r.ReportedByID == id || r.HandledByID == id);
            var hasOrgAnnouncements = await _context.OrgAnnouncements
                .AnyAsync(a => a.PostedByID == id);

            if (user.Announcements.Any() ||
                user.OrganizedEvents.Any() ||
                hasVerifiedOthers ||
                hasApprovedAnnouncements ||
                hasCreatedStudyGroups ||
                hasIncidentReports ||
                hasOrgAnnouncements)
            {
                TempData["Error"] =
                    $"Cannot delete {user.FirstName} {user.LastName} — " +
                    "this user has content records (announcements, events, study groups, " +
                    "incident reports, or approvals) that prevent deletion.";
                return RedirectToAction("Users");
            }

            // Before the cleanup below marks their tag rows deleted.
            await _context.Entry(user).Collection(u => u.UserDepartments).LoadAsync();
            var deleted = await AuditSnapshotAsync(user);

            // Remove all cleanable child records in FK-safe order
            _context.UserAnnouncementInteractions.RemoveRange(
                _context.UserAnnouncementInteractions.Where(i => i.UserID == id));

            _context.Notifications.RemoveRange(
                _context.Notifications.Where(n => n.UserID == id));

            _context.EventWaitlist.RemoveRange(
                _context.EventWaitlist.Where(w => w.UserID == id));

            _context.EventRegistrations.RemoveRange(
                _context.EventRegistrations.Where(r => r.UserID == id));

            _context.OrgMembers.RemoveRange(
                _context.OrgMembers.Where(m => m.UserID == id));

            _context.StudyGroupMembers.RemoveRange(
                _context.StudyGroupMembers.Where(m => m.UserID == id));

            _context.GroupMessages.RemoveRange(
                _context.GroupMessages.Where(m => m.SenderID == id));

            _context.GroupMembers.RemoveRange(
                _context.GroupMembers.Where(m => m.UserID == id));

            _context.Feedbacks.RemoveRange(
                _context.Feedbacks.Where(f => f.UserID == id));

            _context.ChatbotConversations.RemoveRange(
                _context.ChatbotConversations.Where(c => c.UserID == id));

            _context.RefreshTokens.RemoveRange(
                _context.RefreshTokens.Where(t => t.UserID == id));

            // Keep their audit trail; ActorName still says who it was.
            foreach (var log in await _context.AuditLogs.Where(l => l.UserID == id).ToListAsync())
                log.UserID = null;

            _context.PasswordResetTokens.RemoveRange(
                _context.PasswordResetTokens.Where(t => t.UserID == id));

            _context.UserDepartments.RemoveRange(
                _context.UserDepartments.Where(d => d.UserID == id));

            _audit.Record("Delete", AuditArea.Users, user.UserID,
                $"Deleted {user.FirstName} {user.LastName} ({user.Email}).",
                oldValues: deleted);

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"{user.FirstName} {user.LastName}'s account has been permanently deleted.";
            return RedirectToAction("Users");
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/AuditLog
        //  Read-only history of user and structure changes
        // ═══════════════════════════════════════
        public async Task<IActionResult> AuditLog(
            string? actor, string? area, string? actionName,
            DateTime? from, DateTime? to, int page = 1)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(actor))
                query = query.Where(l => l.ActorName != null && l.ActorName.Contains(actor.Trim()));
            if (!string.IsNullOrEmpty(area))
                query = query.Where(l => l.TableAffected == area);
            if (!string.IsNullOrEmpty(actionName))
                query = query.Where(l => l.Action == actionName);
            if (from.HasValue)
                query = query.Where(l => l.CreatedAt >= from.Value.Date);
            if (to.HasValue)
                query = query.Where(l => l.CreatedAt < to.Value.Date.AddDays(1));

            var model = new AuditLogViewModel
            {
                Actor = actor,
                Area = area,
                ActionName = actionName,
                From = from,
                To = to,
                TotalCount = await query.CountAsync(),
                Actions = await _context.AuditLogs
                    .Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync()
            };
            model.Page = Math.Clamp(page, 1, model.PageCount);

            model.Rows = await query
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.LogID)
                .Skip((model.Page - 1) * AuditLogViewModel.PageSize)
                .Take(AuditLogViewModel.PageSize)
                .Select(l => new AuditLogRow
                {
                    CreatedAt = l.CreatedAt,
                    Actor = l.ActorName ?? "System",
                    ActorDeleted = l.UserID == null && l.ActorName != null,
                    Action = l.Action,
                    Area = l.TableAffected,
                    Summary = l.Summary,
                    OldValues = l.OldValues,
                    NewValues = l.NewValues,
                    IPAddress = l.IPAddress
                })
                .ToListAsync();

            return View(model);
        }
    }
}