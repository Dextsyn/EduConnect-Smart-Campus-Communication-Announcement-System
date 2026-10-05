using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly IBlobStorageService _blobStorageService;
        private readonly IHierarchyService _hierarchy;
        private readonly IPlacementService _placement;
        private readonly IAuditService _audit;
        private readonly IAccountEmailService _accountEmails;

        public AccountController(
            ApplicationDbContext context,
            ILogger<AccountController> logger,
            IWebHostEnvironment environment,
            IEmailService emailService,
            INotificationService notificationService,
            IConfiguration configuration,
            IBlobStorageService blobStorageService,
            IHierarchyService hierarchy,
            IPlacementService placement,
            IAuditService audit,
            IAccountEmailService accountEmails)
        {
            _audit = audit;
            _accountEmails = accountEmails;
            _context = context;
            _logger = logger;
            _environment = environment;
            _emailService = emailService;
            _notificationService = notificationService;
            _configuration = configuration;
            _blobStorageService = blobStorageService;
            _hierarchy = hierarchy;
            _placement = placement;
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
        [EnableRateLimiting("auth")]
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
            if (user.VerificationStatus == VerificationStatuses.EmailPending)
            {
                ModelState.AddModelError("",
                    "Confirm your email first. We sent a link to " +
                    "your Adamson inbox when you registered.");
                ViewBag.ResendVerificationEmail = user.Email;
                return View(model);
            }

            if (user.VerificationStatus == VerificationStatuses.Pending)
            {
                ModelState.AddModelError("",
                    "Your email is confirmed. Your account is " +
                    "waiting for verification by ITC.");
                return View(model);
            }

            if (user.VerificationStatus == VerificationStatuses.Rejected)
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

            if (user.MustChangePassword && user.TempPasswordExpiresAt < DateTime.Now)
            {
                ModelState.AddModelError("",
                    "Your temporary password has expired. " +
                    "Please contact ITC to send you a new one.");
                return View(model);
            }

            // Update last login. Logging in with the password we emailed
            // proves the address works.
            user.LastLogin = DateTime.Now;
            user.EmailConfirmedAt ??= DateTime.Now;
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
            HttpContext.Session.SetString("ProfilePicture",
                user.ProfilePicture ?? "");

            if (ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.SetString(ProgramCompletion.SessionKey, "1");

            if (user.MustChangePassword)
                HttpContext.Session.SetString(PasswordChangeRequired.SessionKey, "1");

            // Identifies this login to the browser-presence script in _Layout.
            // A fresh login gets a fresh key, so a stale presence record left
            // by an earlier login can never log this one straight back out.
            HttpContext.Session.SetString("SessionKey",
                Guid.NewGuid().ToString("N"));
            HttpContext.Session.SetString("LoginAtTicks",
                DateTime.UtcNow.Ticks.ToString());

            _logger.LogInformation(
                "User {Email} logged in at {Time}",
                user.Email, DateTime.Now);

            if (user.MustChangePassword)
                return RedirectToAction(nameof(SetPassword));

            return RedirectToDashboard();
        }

        // ─── GET: /Account/Register ────────────
        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var model = new RegisterViewModel
            {
                Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false)
            };

            return View(model);
        }

        // ─── POST: /Account/Register ───────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register(
            RegisterViewModel model)
        {
            // Report a missing program together with every other error.
            if (model.ProgramID == null)
                ModelState.AddModelError("Placement", "Choose a program.");

            if (!ModelState.IsValid)
            {
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
                return View(model);
            }

            // Check if email already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.Email == model.Email);

            // An unconfirmed registration whose link has expired gives the
            // address back, so someone who typed another student's email
            // can't hold it forever.
            if (existingUser != null &&
                existingUser.VerificationStatus == VerificationStatuses.EmailPending &&
                !await _accountEmails.HasLiveVerificationAsync(existingUser.UserID))
            {
                _context.Users.Remove(existingUser);
                await _context.SaveChangesAsync();
                existingUser = null;
            }

            if (existingUser != null)
            {
                ModelState.AddModelError(nameof(RegisterViewModel.EmailUser),
                    existingUser.VerificationStatus == VerificationStatuses.EmailPending
                        ? "This email is already registered and waiting to be confirmed. " +
                          "Check your inbox, or resend the link from the login page."
                        : "Email is already registered.");
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
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
                    r.RoleName == RoleNames.StudentPending);

            if (pendingRole == null)
            {
                ModelState.AddModelError("",
                    "Registration is currently " +
                    "unavailable. Please try again later.");
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
                return View(model);
            }

            // ─── Hash password ─────────────────────
            string hashedPassword = BCrypt.Net.BCrypt
                .HashPassword(model.Password);
            // ─── End hash ──────────────────────────

            // The student confirms their email before ITC sees the account
            // (ConfirmEmail moves it to Pending).
            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = hashedPassword,
                StudentID = model.StudentID,
                RoleID = pendingRole.RoleID,
                IsActive = false,
                VerificationStatus = VerificationStatuses.EmailPending,
                CreatedAt = DateTime.Now
            };

            var placement = await _placement.ApplyAsync(
                user, RoleNames.StudentPending,
                model.CollegeID, model.DepartmentID, model.ProgramID);
            if (!placement.Ok)
            {
                ModelState.AddModelError("Placement", placement.Error!);
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
                return View(model);
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "New registration awaiting email confirmation: {Email}",
                user.Email);

            try
            {
                await _accountEmails.SendVerificationAsync(user);
                TempData["Pending"] =
                    $"Almost done! We sent a confirmation link to {user.Email}. " +
                    "Click it within 24 hours, then ITC will verify your account.";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Verification email failed for {Email}: {Error}",
                    user.Email, ex.Message);
                TempData["Pending"] =
                    "Registration saved, but we couldn't send the confirmation " +
                    "email. Try logging in to resend it.";
            }
            return RedirectToAction("Login");
        }

        // ─── GET: /Account/ConfirmEmail ────────
        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string token)
        {
            var user = await _accountEmails.ConfirmAsync(token);

            if (user == null)
            {
                TempData["Error"] =
                    "This confirmation link is invalid or has expired. " +
                    "Enter your email to get a new one.";
                return RedirectToAction(nameof(ResendVerification));
            }

            if (user.VerificationStatus == VerificationStatuses.EmailPending)
            {
                user.VerificationStatus = VerificationStatuses.Pending;
                user.EmailConfirmedAt = DateTime.Now;
                user.UpdatedAt = DateTime.Now;
            }
            await _context.SaveChangesAsync();

            if (user.VerificationStatus == VerificationStatuses.Pending)
            {
                // Only now does the registration reach ITC's queue.
                var adminIds = await _context.Users
                    .Where(u => u.Role.RoleName == RoleNames.Administrator && u.IsActive)
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
            }

            _logger.LogInformation("Email confirmed: {Email}", user.Email);

            TempData["Pending"] =
                "Email confirmed! ITC will verify your " +
                "account within 1-2 business days.";
            return RedirectToAction("Login");
        }

        // ─── GET: /Account/ResendVerification ──
        [HttpGet]
        public IActionResult ResendVerification(string? email)
        {
            return View(new ResendVerificationViewModel { Email = email ?? "" });
        }

        // ─── POST: /Account/ResendVerification ─
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResendVerification(
            ResendVerificationViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            // Same answer whatever the address, so it can't be used to
            // find out who has registered.
            if (user != null && user.VerificationStatus == VerificationStatuses.EmailPending)
            {
                try
                {
                    await _accountEmails.SendVerificationAsync(user);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Verification email failed for {Email}: {Error}",
                        user.Email, ex.Message);
                }
            }

            TempData["Success"] =
                "If that email is waiting to be confirmed, " +
                "a new link is on its way.";
            return RedirectToAction("Login");
        }

        // ─── GET: /Account/SetPassword ─────────
        [HttpGet]
        public IActionResult SetPassword()
        {
            if (HttpContext.Session.GetString("UserID") == null)
                return RedirectToAction("Login");

            if (HttpContext.Session.GetString(PasswordChangeRequired.SessionKey) != "1")
                return RedirectToDashboard();

            return View(new SetPasswordViewModel());
        }

        // ─── POST: /Account/SetPassword ────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            var userIdStr = HttpContext.Session.GetString("UserID");
            if (userIdStr == null)
                return RedirectToAction("Login");

            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserID == int.Parse(userIdStr));
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            if (BCrypt.Net.BCrypt.Verify(model.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(SetPasswordViewModel.NewPassword),
                    "Choose a password different from the temporary one.");
                return View(model);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            user.MustChangePassword = false;
            user.TempPasswordExpiresAt = null;
            user.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            HttpContext.Session.Remove(PasswordChangeRequired.SessionKey);

            _logger.LogInformation(
                "User {UserID} replaced their temporary password.",
                user.UserID);

            TempData["Success"] = "Password set. Welcome to EduConnect!";
            return RedirectToDashboard();
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
                RoleNames.Administrator => RedirectToAction(
                    "Index", "Admin"),
                RoleNames.Dean => RedirectToAction(
                    "Index", "Dean"),
                RoleNames.Chairperson => RedirectToAction(
                    "Index", "Dean"),
                RoleNames.Faculty => RedirectToAction(
                    "Index", "Faculty"),
                RoleNames.Staff => RedirectToAction(
                    "Index", "Staff"),
                _ => RedirectToAction(
                    "Index", "Home")
            };
        }

        // Placement fields for the profile page. Loads the tree only for
        // students, who are the only ones who edit it here.
        // programLocked: whether the student had a program before this
        // request (a failed save may have set one in memory only).
        private async Task FillPlacementAsync(ProfileViewModel model, User user, bool? programLocked = null)
        {
            // A student picks a program once (registration or first login);
            // after that only the admin moves them.
            var locked = programLocked ?? user.ProgramID != null;
            model.CanEditProgram = user.Role.RoleName == RoleNames.Student && !locked;

            var parts = await _context.Users
                .Where(u => u.UserID == user.UserID)
                .Select(u => new
                {
                    Program = u.AcademicProgram == null ? null : u.AcademicProgram.Name,
                    Department = u.Department == null || u.Department.IsImplicit ? null : u.Department.Name,
                    College = u.College == null ? null : u.College.Name
                })
                .FirstAsync();
            var text = string.Join(" · ", new[] { parts.Program, parts.Department, parts.College }
                .Where(s => !string.IsNullOrEmpty(s)));
            model.PlacementText = text.Length > 0 ? text : null;

            if (model.CanEditProgram)
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
        }

        // ─── GET: /Account/Profile ────────────
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userIdStr = HttpContext.Session.GetString("UserID");
            if (userIdStr == null)
                return RedirectToAction("Login");

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.UserID == int.Parse(userIdStr));

            // Account deleted while logged in: end the session, or Login
            // would bounce it to the dashboard and back here forever.
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            var model = new ProfileViewModel
            {
                FullName = $"{user.FirstName} {(string.IsNullOrEmpty(user.Suffix) ? "" : user.Suffix + " ")}{user.LastName}".Trim(),
                StudentID = user.StudentID,
                Email = user.Email,
                RoleName = user.Role.RoleName,
                ProfilePicturePath = user.ProfilePicture,
                Suffix = user.Suffix,
                CollegeID = user.CollegeID,
                DepartmentID = user.DepartmentID,
                ProgramID = user.ProgramID
            };
            await FillPlacementAsync(model, user);

            // Recomputed on every visit: the admin may have placed the
            // student since they logged in.
            if (ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.SetString(ProgramCompletion.SessionKey, "1");
            else
                HttpContext.Session.Remove(ProgramCompletion.SessionKey);
            ViewBag.MustChooseProgram =
                ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID);

            return View(model);
        }

        // ─── POST: /Account/Profile ───────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(
            ProfileViewModel model)
        {
            var userIdStr = HttpContext.Session.GetString("UserID");
            if (userIdStr == null)
                return RedirectToAction("Login");

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.UserID == int.Parse(userIdStr));

            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            // ─── Suffix allow-list validation ─────
            var allowedSuffixes = new[] { "", "Jr.", "Sr.", "II", "III", "IV", "V" };
            if (model.Suffix != null && !allowedSuffixes.Contains(model.Suffix))
            {
                ModelState.AddModelError("Suffix", "Invalid suffix value.");
            }

            // A student without a program chooses one here; once chosen it
            // is locked and only the admin changes it.
            var hadProgram = user.ProgramID != null;
            if (user.Role.RoleName == RoleNames.Student && !hadProgram)
            {
                var placement = await _placement.ApplyAsync(
                    user, RoleNames.Student,
                    model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
            }

            // ─── Handle profile picture upload ────
            // Only once everything else is valid: the upload replaces and
            // deletes the old avatar, which a failed save must not do.
            if (ModelState.IsValid &&
                model.NewProfilePicture != null &&
                model.NewProfilePicture.Length > 0)
            {
                var allowedExtensions = new[]
                {
                    ".jpg", ".jpeg", ".png", ".webp"
                };
                var allowedContentTypes = new[]
                {
                    "image/jpeg", "image/png", "image/webp"
                };

                var extension = Path.GetExtension(
                    model.NewProfilePicture.FileName ?? string.Empty)
                    .ToLowerInvariant();

                if (!allowedExtensions.Contains(extension) ||
                    !allowedContentTypes.Contains(
                        model.NewProfilePicture.ContentType?.ToLowerInvariant()))
                {
                    ModelState.AddModelError("NewProfilePicture",
                        "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.");
                }
                else if (model.NewProfilePicture.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("NewProfilePicture",
                        "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.");
                }
                else
                {
                    byte[] fileBytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        await model.NewProfilePicture.CopyToAsync(memoryStream);
                        fileBytes = memoryStream.ToArray();
                    }

                    if (!HasValidImageSignature(fileBytes, extension))
                    {
                        ModelState.AddModelError("NewProfilePicture",
                            "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.");
                    }
                    else
                    {
                        var oldProfilePicture = user.ProfilePicture;
                        var fileName = Guid.NewGuid().ToString() + extension;

                        user.ProfilePicture = await _blobStorageService.UploadAsync(
                            fileBytes,
                            fileName,
                            "profiles",
                            model.NewProfilePicture.ContentType);

                        // Delete the old avatar blob now that the new one is stored
                        if (!string.IsNullOrEmpty(oldProfilePicture) &&
                            oldProfilePicture.Contains("/profiles/", StringComparison.OrdinalIgnoreCase))
                        {
                            var oldBlobName = oldProfilePicture.Substring(
                                oldProfilePicture.LastIndexOf('/') + 1);
                            try
                            {
                                await _blobStorageService.DeleteAsync(oldBlobName, "profiles");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex,
                                    "Failed to delete old profile picture blob {BlobName}",
                                    oldBlobName);
                            }
                        }
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                // Re-populate read-only display fields before returning
                model.FullName = $"{user.FirstName} {(string.IsNullOrEmpty(user.Suffix) ? "" : user.Suffix + " ")}{user.LastName}".Trim();
                model.StudentID = user.StudentID;
                model.Email = user.Email;
                model.RoleName = user.Role.RoleName;
                model.ProfilePicturePath = user.ProfilePicture;
                await FillPlacementAsync(model, user, programLocked: hadProgram);
                ViewBag.MustChooseProgram =
                    user.Role.RoleName == RoleNames.Student && !hadProgram;
                return View(model);
            }

            // Update editable fields only
            user.Suffix = string.IsNullOrWhiteSpace(model.Suffix) ? null : model.Suffix;
            user.UpdatedAt = DateTime.Now;

            if (!hadProgram && user.ProgramID != null)
            {
                var program = await _context.Programs
                    .Where(p => p.ProgramID == user.ProgramID)
                    .Select(p => p.Name).FirstOrDefaultAsync();
                _audit.Record("Place", AuditArea.Users, user.UserID,
                    $"{user.FirstName} {user.LastName} chose their program: {program}.");
            }

            await _context.SaveChangesAsync();

            if (!ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.Remove(ProgramCompletion.SessionKey);

            // Refresh session
            HttpContext.Session.SetString("ProfilePicture",
                user.ProfilePicture ?? "");
            HttpContext.Session.SetString("UserName",
                $"{user.FirstName} {(string.IsNullOrEmpty(user.Suffix) ? "" : user.Suffix + " ")}{user.LastName}".Trim());

            TempData["Success"] = "Profile updated successfully.";
            return RedirectToAction("Profile");
        }

        // Verifies file-signature (magic-number) bytes match the
        // claimed image extension, since extension/content-type
        // headers can both be spoofed.
        private static bool HasValidImageSignature(byte[] fileBytes, string extension)
        {
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return fileBytes.Length >= 3 &&
                        fileBytes[0] == 0xFF &&
                        fileBytes[1] == 0xD8 &&
                        fileBytes[2] == 0xFF;

                case ".png":
                    return fileBytes.Length >= 8 &&
                        fileBytes[0] == 0x89 &&
                        fileBytes[1] == 0x50 &&
                        fileBytes[2] == 0x4E &&
                        fileBytes[3] == 0x47 &&
                        fileBytes[4] == 0x0D &&
                        fileBytes[5] == 0x0A &&
                        fileBytes[6] == 0x1A &&
                        fileBytes[7] == 0x0A;

                case ".webp":
                    return fileBytes.Length >= 12 &&
                        fileBytes[0] == 0x52 && // R
                        fileBytes[1] == 0x49 && // I
                        fileBytes[2] == 0x46 && // F
                        fileBytes[3] == 0x46 && // F
                        fileBytes[8] == 0x57 && // W
                        fileBytes[9] == 0x45 && // E
                        fileBytes[10] == 0x42 && // B
                        fileBytes[11] == 0x50;   // P

                default:
                    return false;
            }
        }

        // ─── POST: /Account/ChangePassword ────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            ChangePasswordViewModel model)
        {
            var userIdStr = HttpContext.Session.GetString("UserID");
            if (userIdStr == null)
                return RedirectToAction("Login");

            if (!ModelState.IsValid)
            {
                TempData["ChangePasswordError"] =
                    "Please fix the errors below.";
                return RedirectToAction("Profile");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.UserID == int.Parse(userIdStr));

            if (user == null)
                return RedirectToAction("Login");

            // Verify current password
            bool currentValid = BCrypt.Net.BCrypt
                .Verify(model.CurrentPassword, user.PasswordHash);

            if (!currentValid)
            {
                TempData["ChangePasswordError"] =
                    "Current password is incorrect.";
                return RedirectToAction("Profile");
            }

            // Set new password
            user.PasswordHash = BCrypt.Net.BCrypt
                .HashPassword(model.NewPassword);
            user.MustChangePassword = false;
            user.TempPasswordExpiresAt = null;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            HttpContext.Session.Remove(PasswordChangeRequired.SessionKey);

            _logger.LogInformation(
                "User {UserID} changed their password.",
                user.UserID);

            TempData["Success"] = "Password changed successfully.";
            return RedirectToAction("Profile");
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
        [EnableRateLimiting("auth")]
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

            // They chose this password themselves, so any temporary one
            // is replaced.
            resetToken.User.MustChangePassword = false;
            resetToken.User.TempPasswordExpiresAt = null;

            // Mark token as used
            resetToken.IsUsed = true;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Password reset for {Email}",
                resetToken.User.Email);

            // Notify all admins of the password reset
            var adminIds = await _context.Users
                .Where(u => u.Role.RoleName == RoleNames.Administrator && u.IsActive)
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