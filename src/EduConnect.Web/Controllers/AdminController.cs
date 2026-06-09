using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
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
                .GetString("RoleName") == "Administrator";

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
                .Where(u => u.Role.RoleName == "Faculty" && u.IsActive)
                .CountAsync();

            ViewBag.CountDean = await _context.Users
                .Where(u => u.Role.RoleName == "Dean" && u.IsActive)
                .CountAsync();

            ViewBag.CountChairPerson = await _context.Users
                .Where(u => u.Role.RoleName == "Chair Person" && u.IsActive)
                .CountAsync();

            ViewBag.CountStaff = await _context.Users
                .Where(u => u.Role.RoleName == "Staff" && u.IsActive)
                .CountAsync();

            ViewBag.CountStudent = await _context.Users
                .Where(u => u.Role.RoleName == "Student" && u.IsActive)
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
                    r.RoleName == "Student");

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
        //  POST: /Admin/ToggleUserActive
        //  Activate or Deactivate a user
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserActive(
            int userID)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.UserID == userID);

            if (user != null)
            {
                user.IsActive = !user.IsActive;
                user.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["Success"] = user.IsActive
                    ? $"{user.FirstName}'s account activated."
                    : $"{user.FirstName}'s account deactivated.";
            }

            return RedirectToAction("Users");
        }

        // ═══════════════════════════════════════
        //  POST: /Admin/ChangeUserRole
        //  Change a user's role
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeUserRole(
            int userID, int roleID)
        {
            if (!IsAdmin())
                return RedirectToAction(
                    "Login", "Account");

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.UserID == userID);

            if (user != null)
            {
                var oldRole = user.Role.RoleName;
                user.RoleID = roleID;
                user.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                var newRole = await _context.Roles
                    .FirstOrDefaultAsync(r =>
                        r.RoleID == roleID);

                TempData["Success"] =
                    $"{user.FirstName}'s role changed " +
                    $"from {oldRole} to " +
                    $"{newRole?.RoleName}.";
            }

            return RedirectToAction("Users");
        }
    }
}