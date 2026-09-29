using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class ApprovalService : IApprovalService
    {
        private const string PendingChair = "PendingChair";
        private const string PendingDean = "PendingDean";

        private readonly ApplicationDbContext _context;

        public ApprovalService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ApprovalRouting> RouteAsync(int authorId, bool deanOnly)
        {
            var author = await _context.Users
                .Where(u => u.UserID == authorId)
                .Select(u => new { u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();

            if (author?.CollegeID == null)
                return ApprovalRouting.Fail(
                    "You are not placed in a college yet. Ask your administrator to place you.");

            if (!deanOnly)
            {
                if (author.DepartmentID == null)
                    return ApprovalRouting.Fail(
                        "You are not placed in a department yet. Ask your administrator to place you.");

                var chairs = await _context.Users
                    .Where(u => u.IsActive &&
                                u.Role.RoleName == RoleNames.Chairperson &&
                                u.DepartmentID == author.DepartmentID)
                    .ToListAsync();
                if (chairs.Count > 0)
                    return new ApprovalRouting(true, null, PendingChair, chairs);
            }

            var deans = await _context.Users
                .Where(u => u.IsActive &&
                            u.Role.RoleName == RoleNames.Dean &&
                            u.CollegeID == author.CollegeID)
                .ToListAsync();
            if (deans.Count > 0)
                return new ApprovalRouting(true, null, PendingDean, deans);

            return ApprovalRouting.Fail(deanOnly
                ? "Your college has no Dean to send this to. Contact your administrator."
                : "Your department has no Chairperson and your college has no Dean to review this. Contact your administrator.");
        }

        public IQueryable<Announcement> ReviewableBy(Viewer reviewer)
        {
            var departmentId = reviewer.DepartmentID;
            var collegeId = reviewer.CollegeID;

            // An archived (deleted) post is withdrawn from review.
            var live = _context.Announcements.Where(a => a.Status != "Archived");

            return reviewer.RoleName switch
            {
                RoleNames.Chairperson => live.Where(a =>
                    a.ApprovalStatus == PendingChair &&
                    departmentId != null && a.Author.DepartmentID == departmentId),
                RoleNames.Dean => live.Where(a =>
                    a.ApprovalStatus == PendingDean &&
                    collegeId != null && a.Author.CollegeID == collegeId),
                _ => _context.Announcements.Where(a => false)
            };
        }
    }
}
