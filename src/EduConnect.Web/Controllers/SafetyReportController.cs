using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class SafetyReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<SafetyReportController> _logger;

        public SafetyReportController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<SafetyReportController> logger)
        {
            _context = context;
            _environment = environment;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private string GetBaseUrl() =>
            $"{Request.Scheme}://{Request.Host}";

        // GET /SafetyReport
        public IActionResult Index()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");
            return RedirectToAction("Submit");
        }

        // GET /SafetyReport/Submit
        public IActionResult Submit()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");
            return View();
        }

        // POST /SafetyReport/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SafetyReportViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            string? photoURL = null;
            if (model.Photo != null && model.Photo.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(
                    model.Photo.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension) ||
                    model.Photo.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Photo",
                        "Photo must be a JPG or PNG under 5 MB.");
                    return View(model);
                }

                var uploadFolder = Path.Combine(
                    _environment.WebRootPath,
                    "uploads", "safety-reports");
                Directory.CreateDirectory(uploadFolder);

                var fileName = Guid.NewGuid().ToString() + extension;
                var filePath = Path.Combine(uploadFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await model.Photo.CopyToAsync(stream);

                photoURL = "/uploads/safety-reports/" + fileName;
            }

            var report = new IncidentReport
            {
                ReportedByID = GetUserID(),
                IncidentType = model.Building,
                Description = model.Description,
                Location = model.SpecificLocation,
                PhotoURL = photoURL,
                IsAnonymous = model.IsAnonymous,
                Status = "Pending",
                ReportedAt = DateTime.Now
            };

            _context.IncidentReports.Add(report);
            await _context.SaveChangesAsync();

            await DispatchNotificationsAsync(report);

            return RedirectToAction("Confirmation", new { id = report.ReportID });
        }

        // GET /SafetyReport/Confirmation/{id}
        public async Task<IActionResult> Confirmation(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var report = await _context.IncidentReports
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            return View(report);
        }

        private async Task DispatchNotificationsAsync(IncidentReport report)
        {
            var baseUrl = GetBaseUrl();
            try
            {
                var staffUsers = await _context.Users
                    .Include(u => u.Role)
                    .Where(u => u.Role.RoleName == "Staff"
                                && u.IsActive)
                    .ToListAsync();

                if (!staffUsers.Any()) return;

                var locationPart = string.IsNullOrWhiteSpace(report.Location)
                    ? "No specific location"
                    : report.Location;

                var safeBuilding = System.Net.WebUtility.HtmlEncode(report.IncidentType);
                var safeLocation = System.Net.WebUtility.HtmlEncode(locationPart);
                var safeDescription = System.Net.WebUtility.HtmlEncode(report.Description);

                var message =
                    $"New safety report submitted — " +
                    $"{report.IncidentType}: {locationPart}";
                var link = $"/Staff/ReportDetails/{report.ReportID}";

                var staffIds = staffUsers
                    .Select(u => u.UserID).ToList();

                await _notificationService.SendToManyAsync(
                    staffIds, "SafetyReport", message, link);

                string reporterLine;
                if (report.IsAnonymous)
                {
                    reporterLine = "";
                }
                else
                {
                    var reporter = await _context.Users
                        .Where(u => u.UserID == report.ReportedByID)
                        .Select(u => new { u.FirstName, u.LastName })
                        .FirstOrDefaultAsync();
                    reporterLine = reporter != null
                        ? $"<p><strong>Reported by:</strong> {System.Net.WebUtility.HtmlEncode($"{reporter.FirstName} {reporter.LastName}")}</p>"
                        : "";
                }

                var emailBody = $@"
<h2 style='color:#0d6efd'>New Campus Safety Report</h2>
<table style='border-collapse:collapse;width:100%'>
  <tr><td style='padding:6px;font-weight:bold'>Building</td>
      <td style='padding:6px'>{safeBuilding}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Specific Location</td>
      <td style='padding:6px'>{safeLocation}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Description</td>
      <td style='padding:6px'>{safeDescription}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Submitted At</td>
      <td style='padding:6px'>{report.ReportedAt:yyyy-MM-dd HH:mm}</td></tr>
</table>
{reporterLine}
<p style='margin-top:16px'>
  <a href='{baseUrl}{link}'
     style='background:#0d6efd;color:#fff;padding:8px 16px;
            border-radius:4px;text-decoration:none'>
    View Report in EduConnect
  </a>
</p>";

                foreach (var staff in staffUsers)
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            staff.Email,
                            $"{staff.FirstName} {staff.LastName}",
                            $"New Safety Report — {report.IncidentType}",
                            emailBody);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to email staff {UserID} for report {ReportID}",
                            staff.UserID, report.ReportID);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Notification dispatch failed for report {ReportID}",
                    report.ReportID);
            }
        }
    }
}
