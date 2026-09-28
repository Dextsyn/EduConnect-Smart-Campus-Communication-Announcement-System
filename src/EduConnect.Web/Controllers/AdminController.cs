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

        public AdminController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<AdminController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        // ─── Check if Admin ────────────────────
        private bool IsAdmin() =>
            HttpContext.Session
                .GetString("RoleName") == RoleNames.Administrator;

        private string GetBaseUrl() =>
            $"{Request.Scheme}://{Request.Host}";

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
            string? filterStatus)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var query = _context.Users
                .Include(u => u.Role)
                .Include(u => u.UserDepartments)
                    .ThenInclude(ud => ud.DepartmentTag)
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

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            ViewBag.Users = users;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.FilterRole = filterRole;
            ViewBag.FilterStatus = filterStatus;
            ViewBag.Roles = await _context
                .Roles.ToListAsync();

            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/AddUser
        // ═══════════════════════════════════════
        public async Task<IActionResult> AddUser()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new AdminUserFormViewModel
            {
                Roles = (await _context.Roles.ToListAsync())
                    .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                    .ToList(),
                Departments = (await _context.DepartmentTags
                    .Where(d => d.IsActive)
                    .ToListAsync())
                    .Select(d => new SelectListItem(
                        $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
                    .ToList()
            };
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

            if (!ModelState.IsValid)
            {
                model.Roles = (await _context.Roles.ToListAsync())
                    .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                    .ToList();
                model.Departments = (await _context.DepartmentTags
                    .Where(d => d.IsActive)
                    .ToListAsync())
                    .Select(d => new SelectListItem(
                        $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
                    .ToList();
                return View(model);
            }

            var adminID = int.Parse(HttpContext.Session.GetString("UserID"));

            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                StudentID = model.StudentID,
                RoleID = model.RoleID,
                IsActive = model.IsActive,
                VerificationStatus = "Verified",
                VerifiedByID = adminID,
                VerifiedAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _context.UserDepartments.Add(new UserDepartment
            {
                UserID = user.UserID,
                TagID = model.DepartmentTagID,
                IsPrimary = true,
                CreatedAt = DateTime.Now
            });
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

            var primaryDept = user.UserDepartments
                .FirstOrDefault(ud => ud.IsPrimary);

            var model = new AdminUserFormViewModel
            {
                UserID = user.UserID,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                StudentID = user.StudentID,
                RoleID = user.RoleID,
                DepartmentTagID = primaryDept?.TagID ?? 0,
                IsActive = user.IsActive,
                Roles = (await _context.Roles.ToListAsync())
                    .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                    .ToList(),
                Departments = (await _context.DepartmentTags
                    .Where(d => d.IsActive)
                    .ToListAsync())
                    .Select(d => new SelectListItem(
                        $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
                    .ToList()
            };

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
                model.Roles = (await _context.Roles.ToListAsync())
                    .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                    .ToList();
                model.Departments = (await _context.DepartmentTags
                    .Where(d => d.IsActive)
                    .ToListAsync())
                    .Select(d => new SelectListItem(
                        $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
                    .ToList();
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

            // Replace primary department
            var existingPrimary = user.UserDepartments
                .FirstOrDefault(ud => ud.IsPrimary);

            if (existingPrimary != null && existingPrimary.TagID != model.DepartmentTagID)
            {
                _context.UserDepartments.Remove(existingPrimary);
                _context.UserDepartments.Add(new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = model.DepartmentTagID,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                });
            }
            else if (existingPrimary == null)
            {
                _context.UserDepartments.Add(new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = model.DepartmentTagID,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                });
            }

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

            _context.AuditLogs.RemoveRange(
                _context.AuditLogs.Where(l => l.UserID == id));

            _context.PasswordResetTokens.RemoveRange(
                _context.PasswordResetTokens.Where(t => t.UserID == id));

            _context.UserDepartments.RemoveRange(
                _context.UserDepartments.Where(d => d.UserID == id));

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"{user.FirstName} {user.LastName}'s account has been permanently deleted.";
            return RedirectToAction("Users");
        }

        // ═══════════════════════════════════════
        //  DEPARTMENTS
        // ═══════════════════════════════════════

        // The School Wide row, whose ShortName "ALL" is matched as a literal
        // string by the announcement feed (FeedRankingService,
        // AnnouncementController, HomeController, ChatbotService). Renaming
        // or retiring it would silently stop campus-wide announcements from
        // reaching anyone, so its ShortName, type and active state are locked.
        private static readonly string[] SystemShortNames = { "ALL" };

        private static bool IsSystemDepartment(DepartmentTag dept) =>
            dept.ShortName != null &&
            SystemShortNames.Contains(
                dept.ShortName, StringComparer.OrdinalIgnoreCase);

        private async Task PopulateDepartmentFormDropdowns(
            AdminDepartmentFormViewModel model)
        {
            model.TagTypes = (await _context.TagTypes
                    .OrderBy(t => t.TagTypeID)
                    .ToListAsync())
                .Select(t => new SelectListItem(
                    t.TypeName, t.TagTypeID.ToString()))
                .ToList();
        }

        // Shared by both POST actions. On create TagID is 0, which never
        // matches an existing row, so the same expression covers both cases.
        private async Task ValidateDepartmentAsync(
            AdminDepartmentFormViewModel model)
        {
            // TagName carries a UNIQUE index — without this pre-check EF
            // throws DbUpdateException and the user gets a 500, not an error.
            if (!string.IsNullOrWhiteSpace(model.TagName) &&
                await _context.DepartmentTags.AnyAsync(d =>
                    d.TagName == model.TagName && d.TagID != model.TagID))
                ModelState.AddModelError("TagName",
                    "A department with this name already exists.");

            if (!string.IsNullOrWhiteSpace(model.ShortName) &&
                await _context.DepartmentTags.AnyAsync(d =>
                    d.ShortName == model.ShortName && d.TagID != model.TagID))
                // ShortName has no unique index, but the feed treats it as a key
                ModelState.AddModelError("ShortName",
                    "A department with this short code already exists.");
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/Departments
        //  Manage department tags
        // ═══════════════════════════════════════
        public async Task<IActionResult> Departments(
            string? filterType,
            string? filterStatus)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var query = _context.DepartmentTags
                .Include(d => d.TagType)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filterType))
                query = query.Where(d =>
                    d.TagType.TypeName == filterType);

            if (filterStatus == "Active")
                query = query.Where(d => d.IsActive);
            else if (filterStatus == "Inactive")
                query = query.Where(d => !d.IsActive);

            var departments = await query
                .OrderBy(d => d.TagType.TagTypeID)
                .ThenBy(d => d.TagName)
                .ToListAsync();

            // Counted up front — doing this per row in the view would N+1
            ViewBag.UserCounts = await _context.UserDepartments
                .GroupBy(ud => ud.TagID)
                .Select(g => new { TagID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TagID, x => x.Count);

            ViewBag.AnnouncementCounts = await _context.AnnouncementTags
                .GroupBy(at => at.TagID)
                .Select(g => new { TagID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TagID, x => x.Count);

            ViewBag.Departments = departments;
            ViewBag.TagTypes = await _context.TagTypes
                .OrderBy(t => t.TagTypeID)
                .ToListAsync();
            ViewBag.FilterType = filterType;
            ViewBag.FilterStatus = filterStatus;
            ViewBag.SystemShortNames = SystemShortNames;

            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/AddDepartment
        // ═══════════════════════════════════════
        public async Task<IActionResult> AddDepartment()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new AdminDepartmentFormViewModel();
            await PopulateDepartmentFormDropdowns(model);
            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/AddDepartment
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(
            AdminDepartmentFormViewModel model)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            model.TagName = model.TagName?.Trim();
            model.ShortName = model.ShortName?.Trim();

            // A new department must not claim one of the reserved codes
            if (!string.IsNullOrWhiteSpace(model.ShortName) &&
                SystemShortNames.Contains(
                    model.ShortName, StringComparer.OrdinalIgnoreCase))
                ModelState.AddModelError("ShortName",
                    $"\"{model.ShortName}\" is reserved by the announcement " +
                    "feed and cannot be used for a new department.");

            await ValidateDepartmentAsync(model);

            if (!ModelState.IsValid)
            {
                await PopulateDepartmentFormDropdowns(model);
                return View(model);
            }

            var dept = new DepartmentTag
            {
                TagName = model.TagName,
                ShortName = model.ShortName,
                TagTypeID = model.TagTypeID,
                Description = string.IsNullOrWhiteSpace(model.Description)
                    ? null
                    : model.Description.Trim(),
                ColorHex = model.ColorHex,
                IsActive = model.IsActive,
                CreatedAt = DateTime.Now
            };

            _context.DepartmentTags.Add(dept);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"\"{dept.TagName}\" has been created.";
            return RedirectToAction("Departments");
        }

        // ═══════════════════════════════════════
        //  GET: /Admin/EditDepartment/5
        // ═══════════════════════════════════════
        public async Task<IActionResult> EditDepartment(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var dept = await _context.DepartmentTags
                .FirstOrDefaultAsync(d => d.TagID == id);

            if (dept == null)
            {
                TempData["Error"] = "Department not found.";
                return RedirectToAction("Departments");
            }

            var model = new AdminDepartmentFormViewModel
            {
                TagID = dept.TagID,
                TagName = dept.TagName,
                ShortName = dept.ShortName,
                TagTypeID = dept.TagTypeID,
                Description = dept.Description,
                ColorHex = dept.ColorHex,
                IsActive = dept.IsActive,
                IsSystemDepartment = IsSystemDepartment(dept)
            };

            await PopulateDepartmentFormDropdowns(model);
            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/EditDepartment/5
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDepartment(
            int id, AdminDepartmentFormViewModel model)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var dept = await _context.DepartmentTags
                .FirstOrDefaultAsync(d => d.TagID == id);

            if (dept == null)
            {
                TempData["Error"] = "Department not found.";
                return RedirectToAction("Departments");
            }

            model.TagID = id;
            model.TagName = model.TagName?.Trim();
            model.ShortName = model.ShortName?.Trim();
            model.IsSystemDepartment = IsSystemDepartment(dept);

            // A disabled input posts nothing and a hostile client can post
            // anything, so never trust the form for these three fields.
            if (model.IsSystemDepartment)
            {
                model.ShortName = dept.ShortName;
                model.TagTypeID = dept.TagTypeID;
                model.IsActive = dept.IsActive;
                ModelState.Remove(nameof(model.ShortName));
                ModelState.Remove(nameof(model.TagTypeID));
                ModelState.Remove(nameof(model.IsActive));
            }

            await ValidateDepartmentAsync(model);

            if (!ModelState.IsValid)
            {
                await PopulateDepartmentFormDropdowns(model);
                return View(model);
            }

            dept.TagName = model.TagName;
            dept.ShortName = model.ShortName;
            dept.TagTypeID = model.TagTypeID;
            dept.Description = string.IsNullOrWhiteSpace(model.Description)
                ? null
                : model.Description.Trim();
            dept.ColorHex = model.ColorHex;
            dept.IsActive = model.IsActive;
            dept.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"\"{dept.TagName}\" has been updated.";
            return RedirectToAction("Departments");
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/ToggleDepartment/5
        //  Retire or restore — never a hard delete,
        //  every FK into DepartmentTags is Restrict
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDepartment(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var dept = await _context.DepartmentTags
                .FirstOrDefaultAsync(d => d.TagID == id);

            if (dept == null)
            {
                TempData["Error"] = "Department not found.";
                return RedirectToAction("Departments");
            }

            if (IsSystemDepartment(dept))
            {
                TempData["Error"] =
                    $"\"{dept.TagName}\" is referenced by the announcement " +
                    "feed and cannot be retired.";
                return RedirectToAction("Departments");
            }

            dept.IsActive = !dept.IsActive;
            dept.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["Success"] = dept.IsActive
                ? $"\"{dept.TagName}\" has been restored."
                : $"\"{dept.TagName}\" has been retired.";

            return RedirectToAction("Departments");
        }
    }
}