using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class PersonalizedFeed
    {
        public List<AnnouncementTableViewModel> Department { get; set; } = new();
        public List<AnnouncementTableViewModel> ForYou { get; set; } = new();
    }

    public interface IFeedRankingService
    {
        Task<PersonalizedFeed> GetPersonalizedFeedAsync(
            Viewer viewer,
            string? searchQuery,
            string? filterFeedType);
    }

    public class FeedRankingService : IFeedRankingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAudienceService _audience;

        public FeedRankingService(ApplicationDbContext context, IAudienceService audience)
        {
            _context = context;
            _audience = audience;
        }

        public async Task<PersonalizedFeed> GetPersonalizedFeedAsync(
            Viewer viewer,
            string? searchQuery,
            string? filterFeedType)
        {
            var userID = viewer.UserID;
            var result = new PersonalizedFeed();

            // Load user's interaction history (last 90 days) for affinity
            var historyCutoff = DateTime.Now.AddDays(-90);
            var interactions = await _context.UserAnnouncementInteractions
                .Where(i => i.UserID == userID && i.ViewedAt >= historyCutoff)
                .Include(i => i.Announcement)
                .ToListAsync();

            var totalViews = interactions.Count;
            var categoryAffinity = new Dictionary<int, double>();
            var feedTypeAffinity = new Dictionary<string, double>();

            if (totalViews > 0)
            {
                categoryAffinity = interactions
                    .GroupBy(i => i.Announcement.CategoryID)
                    .ToDictionary(g => g.Key, g => (double)g.Count() / totalViews);

                feedTypeAffinity = interactions
                    .GroupBy(i => i.Announcement.FeedType)
                    .ToDictionary(g => g.Key, g => (double)g.Count() / totalViews);
            }

            // Fetch all announcements the student is authorized to see
            var baseQuery = _context.Announcements
                .Include(a => a.Category)
                .Include(a => a.Author)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .Where(a => a.Status == "Published" &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                .Where(_audience.VisibleTo(viewer));

            if (!string.IsNullOrEmpty(searchQuery))
                baseQuery = baseQuery.Where(a =>
                    a.Title.Contains(searchQuery) || a.Body.Contains(searchQuery));

            if (!string.IsNullOrEmpty(filterFeedType))
                baseQuery = baseQuery.Where(a => a.FeedType == filterFeedType);

            var authorized = await baseQuery.ToListAsync();

            // ── Section 1: Your Department ────────────────────────────────
            // Addressed to the student's program/department/college or tags
            // (not just School Wide), last 30 days
            var addressedIDs = (await baseQuery
                .Where(_audience.AddressedTo(viewer))
                .Select(a => a.AnnouncementID)
                .ToListAsync()).ToHashSet();

            var deptCutoff = DateTime.Now.AddDays(-30);
            var deptAnnouncements = authorized
                .Where(a =>
                    addressedIDs.Contains(a.AnnouncementID) &&
                    a.PublishedAt >= deptCutoff)
                .OrderByDescending(a => a.IsEmergency)
                .ThenByDescending(a => a.Priority)
                .ThenByDescending(a => a.PublishedAt)
                .Take(5)
                .ToList();

            var deptIDs = deptAnnouncements
                .Select(a => a.AnnouncementID)
                .ToHashSet();

            result.Department = deptAnnouncements
                .Select(ToViewModel)
                .ToList();

            // ── Section 2: For You ────────────────────────────────────────
            // All authorized announcements, excluding dept-section items, ranked by score
            var forYouCandidates = authorized
                .Where(a => !deptIDs.Contains(a.AnnouncementID))
                .Select(a => (Announcement: a, Score: ComputeScore(a, categoryAffinity, feedTypeAffinity)))
                .OrderByDescending(x => x.Score)
                .Take(5)
                .ToList();

            result.ForYou = forYouCandidates
                .Select(x => ToViewModel(x.Announcement))
                .ToList();

            // There is deliberately no section for announcements outside the
            // student's audience: students read only what is addressed to
            // them (the old "Explore" section was removed on 2026-10-31).
            await _audience.AddTargetLabelsAsync(
                result.Department.Concat(result.ForYou));

            return result;
        }

        private static double ComputeScore(
            Announcement a,
            Dictionary<int, double> categoryAffinity,
            Dictionary<string, double> feedTypeAffinity)
        {
            double score = 0;

            if (a.IsEmergency)
                score += 10000;

            // Priority 1–5 → 0–400
            score += (a.Priority - 1) * 100.0;

            // Recency: exponential decay with 14-day half-life → 0–200
            if (a.PublishedAt.HasValue)
            {
                var daysSince = (DateTime.Now - a.PublishedAt.Value).TotalDays;
                score += Math.Exp(-daysSince / 14.0) * 200;
            }

            // Category affinity → 0–300
            if (categoryAffinity.TryGetValue(a.CategoryID, out var catAff))
                score += catAff * 300;

            // FeedType affinity → 0–150
            if (feedTypeAffinity.TryGetValue(a.FeedType, out var ftAff))
                score += ftAff * 150;

            // Global popularity: capped at 100 views → 0–50
            score += Math.Min(a.ViewCount / 100.0, 1.0) * 50;

            return score;
        }

        private static AnnouncementTableViewModel ToViewModel(Announcement a) =>
            new AnnouncementTableViewModel
            {
                AnnouncementID = a.AnnouncementID,
                Title = a.Title,
                CategoryName = a.Category.CategoryName,
                CategoryColor = a.Category.ColorHex,
                FeedType = a.FeedType,
                IsEmergency = a.IsEmergency,
                AuthorName = a.Author.FirstName + " " + a.Author.LastName,
                Status = a.Status,
                ViewCount = a.ViewCount,
                PublishedAt = a.PublishedAt,
                Tags = a.AnnouncementTags
                    .Select(at => at.DepartmentTag.ShortName)
                    .ToList()
            };
    }
}
