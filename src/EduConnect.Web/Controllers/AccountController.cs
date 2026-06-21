using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EduConnect.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;

        public AccountController(
            ApplicationDbContext context,
            ILogger<AccountController> logger,
            IWebHostEnvironment environment,
            IEmailService emailService,
            INotificationService notificationService,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
            _emailService = emailService;
            _notificationService = notificationService;
            _configuration = configuration;
        }

        // ─── GET: /Account/Login ───────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (HttpContext.Session
                .GetString("UserID") != null)
                return RedirectToDashboard();

            return View();
        }

        // ─── POST: /Account/Login ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.Email == model.Email);

            if (user == null)
            {
                ModelState.AddModelError("",
                    "Invalid email or password.");
                return View(model);
            }

            // Check verification status
            if (user.VerificationStatus == "Pending")
            {
                ModelState.AddModelError("",
                    "Your account is pending " +
                    "verification by ITC. " +
                    "Please wait for approval.");
                return View(model);
            }

            if (user.VerificationStatus == "Rejected")
            {
                ModelState.AddModelError("",
                    "Your account was not verified. " +
                    "Reason: " +
                    user.VerificationRejectionReason +
                    " Please contact ITC.");
                return View(model);
            }

            // Check if account is active
            if (!user.IsActive)
            {
                ModelState.AddModelError("",
                    "Your account has been deactivated. " +
                    "Please contact ITC.");
                return View(model);
            }

            // Verify password
            bool isValidPassword = BCrypt.Net.BCrypt
                .Verify(model.Password, user.PasswordHash);

            if (!isValidPassword)
            {
                ModelState.AddModelError("",
                    "Invalid email or password.");
                return View(model);
            }

            // Update last login
            user.LastLogin = DateTime.Now;
            await _context.SaveChangesAsync();

            // Store session
            HttpContext.Session.SetString("UserID",
                user.UserID.ToString());
            HttpContext.Session.SetString("UserName",
                $"{user.FirstName} {user.LastName}");
            HttpContext.Session.SetString("UserEmail",
                user.Email);
            HttpContext.Session.SetString("RoleID",
                user.RoleID.ToString());
            HttpContext.Session.SetString("RoleName",
                user.Role.RoleName);

            _logger.LogInformation(
                "User {Email} logged in at {Time}",
                user.Email, DateTime.Now);

            return RedirectToDashboard();
        }

        // ─── GET: /Account/Register ────────────
        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var model = new RegisterViewModel
            {
                Departments = await _context.DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           d.TagType.TypeName == "Academic")
                    .OrderBy(d => d.TagName)
                    .ToListAsync()
            };

            return View(model);
        }

        // ─── POST: /Account/Register ───────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(
            RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Departments = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           d.TagType.TypeName == "Academic")
                    .OrderBy(d => d.TagName)
                    .ToListAsync();
                return View(model);
            }

            // Check if email already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.Email == model.Email);

            if (existingUser != null)
            {
                ModelState.AddModelError("Email",
                    "Email is already registered.");
                model.Departments = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           d.TagType.TypeName == "Academic")
                    .OrderBy(d => d.TagName)
                    .ToListAsync();
                return View(model);
            }

            // ─── STEP 7 — Handle ID Photo Upload ──
           /* string? schoolIDPhotoURL = null;
            if (model.SchoolIDPhoto != null &&
                model.SchoolIDPhoto.Length > 0)
            {
                var uploadsFolder = Path.Combine(
                    _environment.WebRootPath,
                    "uploads", "school-ids");

                Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString()
                    + Path.GetExtension(
                        model.SchoolIDPhoto.FileName);

                var filePath = Path.Combine(
                    uploadsFolder, fileName);

                using var stream = new FileStream(
                    filePath, FileMode.Create);
                await model.SchoolIDPhoto
                    .CopyToAsync(stream);

                schoolIDPhotoURL =
                    "/uploads/school-ids/" + fileName;
            }
           */
            // ─── END OF STEP 7 ────────────────────

            // Get Student Pending role
            var pendingRole = await _context.Roles
                .FirstOrDefaultAsync(r =>
                    r.RoleName == "Student Pending");

            if (pendingRole == null)
            {
                ModelState.AddModelError("",
                    "Registration is currently " +
                    "unavailable. Please try again later.");
                return View(model);
            }

            // ─── Hash password ─────────────────────
            string hashedPassword = BCrypt.Net.BCrypt
                .HashPassword(model.Password);
            // ─── End hash ──────────────────────────

            // Create user with Pending status
            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = hashedPassword,
                StudentID = model.StudentID,
                RoleID = pendingRole.RoleID,
                IsActive = false,
                VerificationStatus = "Pending",
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Assign department
            if (model.DepartmentTagID.HasValue)
            {
                var userDept = new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = model.DepartmentTagID.Value,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                };
                _context.UserDepartments.Add(userDept);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation(
                "New registration pending: {Email}",
                user.Email);

            // Notify all admins of the new pending student
            var adminIds = await _context.Users
                .Where(u => u.Role.RoleName == "Administrator" && u.IsActive)
                .Select(u => u.UserID)
                .ToListAsync();
            if (adminIds.Count > 0)
            {
                await _notificationService.SendToManyAsync(
                    adminIds,
                    "NewPendingStudent",
                    $"New student pending approval: {user.FirstName} {user.LastName}",
                    "/Admin/PendingVerifications");
            }

            TempData["Pending"] =
                "Registration submitted! ITC will " +
                "verify your account within " +
                "1-2 business days.";
            return RedirectToAction("Login");
        }

        // ─── GET: /Account/Logout ──────────────
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ─── Helper: Redirect by role ──────────
        private IActionResult RedirectToDashboard()
        {
            var role = HttpContext.Session
                .GetString("RoleName");

            return role switch
            {
                "Administrator" => RedirectToAction(
                    "Index", "Admin"),
                "Dean" => RedirectToAction(
                    "Index", "Dean"),
                "Chair Person" => RedirectToAction(
                    "Index", "Dean"),
                "Faculty" => RedirectToAction(
                    "Index", "Faculty"),
                "Staff" => RedirectToAction(
                    "Index", "Staff"),
                _ => RedirectToAction(
                    "Index", "Home")
            };
        }

        // ─── GET: /Account/ForgotPassword ─────
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // ─── POST: /Account/ForgotPassword ────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(
            ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Find user by email
            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.Email == model.Email);

            // Always show success even if
            // email not found — security best practice
            // Prevents email enumeration attacks
            if (user == null)
            {
                TempData["Success"] =
                    "If that email exists in our system " +
                    "you will receive a reset link shortly.";
                return RedirectToAction("Login");
            }

            // Generate secure token
            var token = Convert.ToBase64String(
                Guid.NewGuid().ToByteArray()) +
                Convert.ToBase64String(
                Guid.NewGuid().ToByteArray());

            // Remove any existing tokens for this user
            var existingTokens = await _context
                .PasswordResetTokens
                .Where(t => t.UserID == user.UserID &&
                            !t.IsUsed)
                .ToListAsync();

            _context.PasswordResetTokens
                .RemoveRange(existingTokens);

            // Save new token
            var resetToken = new PasswordResetToken
            {
                UserID = user.UserID,
                Token = token,
                ExpiresAt = DateTime.Now.AddHours(1),
                IsUsed = false,
                CreatedAt = DateTime.Now
            };

            _context.PasswordResetTokens.Add(resetToken);
            await _context.SaveChangesAsync();

            // Build reset URL
            var baseUrl = _configuration["AppBaseUrl"]?.TrimEnd('/')
                ?? $"{Request.Scheme}://{Request.Host}";
            var resetURL = $"{baseUrl}/Account/ResetPassword?token={Uri.EscapeDataString(token)}";

            // Send email
            var emailBody = $@"
    <div style='font-family: Arial, sans-serif;
                max-width: 600px; margin: 0 auto;'>

        <div style='background: #0d6efd;
                    padding: 30px;
                    text-align: center;
                    border-radius: 8px 8px 0 0;'>
            <h1 style='color: white; margin: 0;'>
                EduConnect
            </h1>
            <p style='color: rgba(255,255,255,0.8);
                      margin: 5px 0 0;'>
                Adamson University
            </p>
        </div>

        <div style='background: #f8f9fa;
                    padding: 30px;
                    border-radius: 0 0 8px 8px;'>
            <h2 style='color: #333;'>
                Password Reset Request
            </h2>
            <p style='color: #666;'>
                Hi {user.FirstName},
            </p>
            <p style='color: #666;'>
                We received a request to reset
                your EduConnect password.
                Click the button below to reset it.
            </p>
            <p style='color: #666;'>
                This link will expire in
                <strong>1 hour</strong>.
            </p>

            <div style='text-align: center;
                        margin: 30px 0;'>
                <a href='{resetURL}'
                   style='background: #0d6efd;
                           color: white;
                           padding: 14px 30px;
                           text-decoration: none;
                           border-radius: 6px;
                           font-weight: bold;
                           display: inline-block;'>
                    Reset My Password
                </a>
            </div>

            <p style='color: #999; font-size: 13px;'>
                If you did not request this,
                please ignore this email.
                Your password will remain unchanged.
            </p>

            <hr style='border: none;
                        border-top: 1px solid #ddd;
                        margin: 20px 0;' />

            <p style='color: #999; font-size: 12px;'>
                Or copy this link to your browser:
                <br/>
                <a href='{resetURL}'
                   style='color: #0d6efd;
                           word-break: break-all;'>
                    {resetURL}
                </a>
            </p>
        </div>
    </div>";

            try
            {
                await _emailService.SendEmailAsync(
                    user.Email,
                    $"{user.FirstName} {user.LastName}",
                    "EduConnect — Password Reset Request",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Failed to send reset email: {Error}",
                    ex.Message);
            }

            TempData["Success"] =
                "If that email exists in our system " +
                "you will receive a reset link shortly.";
            return RedirectToAction("Login");
        }

        // ─── GET: /Account/ResetPassword ──────
        [HttpGet]
        public async Task<IActionResult> ResetPassword(
            string token)
        {
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Login");

            // Validate token
            var resetToken = await _context
                .PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t =>
                    t.Token == token &&
                    !t.IsUsed &&
                    t.ExpiresAt > DateTime.Now);

            if (resetToken == null)
            {
                TempData["Error"] =
                    "This password reset link is invalid " +
                    "or has expired. Please request a new one.";
                return RedirectToAction("ForgotPassword");
            }

            var model = new ResetPasswordViewModel
            {
                Token = token,
                Email = resetToken.User.Email
            };

            return View(model);
        }

        // ─── POST: /Account/ResetPassword ─────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(
            ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Validate token again
            var resetToken = await _context
                .PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t =>
                    t.Token == model.Token &&
                    !t.IsUsed &&
                    t.ExpiresAt > DateTime.Now);

            if (resetToken == null)
            {
                TempData["Error"] =
                    "This link is invalid or expired. " +
                    "Please request a new one.";
                return RedirectToAction("ForgotPassword");
            }

            // Hash new password
            string newHash = BCrypt.Net.BCrypt
                .HashPassword(model.NewPassword);

            // Update password
            resetToken.User.PasswordHash = newHash;
            resetToken.User.UpdatedAt = DateTime.Now;

            // Mark token as used
            resetToken.IsUsed = true;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Password reset for {Email}",
                resetToken.User.Email);

            // Notify all admins of the password reset
            var adminIds = await _context.Users
                .Where(u => u.Role.RoleName == "Administrator" && u.IsActive)
                .Select(u => u.UserID)
                .ToListAsync();
            if (adminIds.Count > 0)
            {
                await _notificationService.SendToManyAsync(
                    adminIds,
                    "PasswordReset",
                    $"Student {resetToken.User.FirstName} {resetToken.User.LastName} reset their password.",
                    "/Admin/UserManagement");
            }

            TempData["Success"] =
                "Password reset successfully! " +
                "You can now login with your new password.";
            return RedirectToAction("Login");
        }
    }
}