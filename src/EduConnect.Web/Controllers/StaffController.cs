using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        // The statuses staff can set, in the order the form offers them.
        public static readonly IReadOnlyList<string> ReportStatuses =
            new[] { "Pending", "Investigating", "Resolved", "Dismissed" };

        public StaffController(
            ApplicationDbContext context,
            INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        private bool IsStaffOrAdmin()
        {
            var role = HttpContext.Session.GetString("RoleName");
            return role == RoleNames.Staff || role == RoleNames.Administrator;
        }

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        // GET /Staff
        public async Task<IActionResult> Index(
            SafetyReportFilterViewModel filter)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var query = _context.IncidentReports
                .Include(r => r.ReportedBy)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter.Building))
                query = query.Where(r =>
                    r.IncidentType == filter.Building);

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(r =>
                    r.Status == filter.Status);

            var reports = await query
                .OrderByDescending(r => r.ReportedAt)
                .ToListAsync();

            ViewBag.Filter = filter;
            return View(reports);
        }

        // GET /Staff/ReportDetails/{id}
        public async Task<IActionResult> ReportDetails(int id)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var report = await _context.IncidentReports
                .Include(r => r.ReportedBy)
                .Include(r => r.HandledBy)
                .Include(r => r.Activities)
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            return View(report);
        }

        // POST /Staff/UpdateStatus/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(
            int id, string status, string? resolution)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            // The form only offers these; anything else was not sent by it.
            if (!ReportStatuses.Contains(status))
                return BadRequest();

            var report = await _context.IncidentReports
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            var note = string.IsNullOrWhiteSpace(resolution)
                ? null
                : resolution.Trim();
            var action =
                report.Status != status ? IncidentReportActivity.StatusChanged
                : report.Resolution != note ? IncidentReportActivity.NoteUpdated
                : null;

            if (action == null)
            {
                TempData["Success"] = "Nothing changed.";
                return RedirectToAction("ReportDetails", new { id });
            }

            var userId = GetUserID();
            _context.IncidentReportActivities.Add(new IncidentReportActivity
            {
                ReportID = report.ReportID,
                ActorID = userId,
                ActorName = await ActorNameAsync(userId),
                Action = action,
                FromStatus = report.Status,
                ToStatus = status,
                Note = note
            });

            report.Status = status;
            report.Resolution = note;
            report.HandledByID = userId;

            if (status == "Resolved" || status == "Dismissed")
                report.ResolvedAt = DateTime.Now;
            else
                report.ResolvedAt = null;

            await _context.SaveChangesAsync();

            // The reporter sees status and note on My Reports; tell them
            // something there changed.
            if (report.ReportedByID is int reporterId)
                await _notificationService.SendAsync(
                    reporterId,
                    "SafetyReport",
                    action == IncidentReportActivity.StatusChanged
                        ? $"Your safety report #{report.ReportID} is now {status}"
                        : $"Campus staff updated the note on your safety report #{report.ReportID}",
                    "/SafetyReport/MyReports");

            TempData["Success"] = "Report status updated.";
            return RedirectToAction("ReportDetails", new { id });
        }

        // GET /Staff/Activity
        public async Task<IActionResult> Activity(
            StaffActivityFilterViewModel filter)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var userId = GetUserID();
            if (!filter.IsAll)
                filter.Scope = StaffActivityFilterViewModel.Mine;
            if (filter.Page < 1)
                filter.Page = 1;

            // Period and building narrow everything. The work counts
            // follow the scope; Received counts every report that came
            // in, since every staff member receives every report.
            var period = _context.IncidentReportActivities.AsQueryable();
            if (filter.From.HasValue)
                period = period.Where(a => a.CreatedAt >= filter.From.Value.Date);
            if (filter.To.HasValue)
            {
                var end = filter.To.Value.Date.AddDays(1);
                period = period.Where(a => a.CreatedAt < end);
            }
            if (!string.IsNullOrEmpty(filter.Building))
                period = period.Where(a => a.Report.IncidentType == filter.Building);

            var work = period.Where(a => a.Action != IncidentReportActivity.Received);
            if (!filter.IsAll)
                work = work.Where(a => a.ActorID == userId);

            var rows = filter.IsAll ? period : work;
            if (!string.IsNullOrEmpty(filter.Status))
                rows = rows.Where(a => a.ToStatus == filter.Status);

            var model = new StaffActivityViewModel
            {
                Filter = filter,
                ReceivedCount = await period.CountAsync(
                    a => a.Action == IncidentReportActivity.Received),
                ResolvedCount = await work.CountAsync(a =>
                    a.Action == IncidentReportActivity.StatusChanged
                    && a.ToStatus == "Resolved"),
                DismissedCount = await work.CountAsync(a =>
                    a.Action == IncidentReportActivity.StatusChanged
                    && a.ToStatus == "Dismissed"),
                ActionCount = await work.CountAsync(),
                TotalRows = await rows.CountAsync(),
                Rows = await rows
                    .Include(a => a.Report)
                        .ThenInclude(r => r.ReportedBy)
                    .OrderByDescending(a => a.CreatedAt)
                    .ThenByDescending(a => a.ActivityID)
                    .Skip((filter.Page - 1) * StaffActivityViewModel.PageSize)
                    .Take(StaffActivityViewModel.PageSize)
                    .ToListAsync()
            };

            return View(model);
        }

        private async Task<string?> ActorNameAsync(int userId) =>
            await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync();
    }
}
