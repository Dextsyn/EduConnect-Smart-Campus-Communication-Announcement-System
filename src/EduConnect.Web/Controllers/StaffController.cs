using EduConnect.Web.Data;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool IsStaffOrAdmin()
        {
            var role = HttpContext.Session.GetString("RoleName");
            return role == "Staff" || role == "Administrator";
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

            var report = await _context.IncidentReports
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            report.Status = status;
            report.Resolution = resolution;
            report.HandledByID = GetUserID();

            if (status == "Resolved" || status == "Dismissed")
                report.ResolvedAt = DateTime.Now;
            else
                report.ResolvedAt = null;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Report status updated.";
            return RedirectToAction("ReportDetails", new { id });
        }
    }
}
