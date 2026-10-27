using EduConnect.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EduConnect.Web.Services
{
    public class ContentModerationService : IContentModerationService
    {
        // One app instance (no SignalR backplane), so clearing this on an
        // admin edit is enough to keep every request current.
        private const string CacheKey = "ModerationMatcher";

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public ContentModerationService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<bool> ContainsProhibitedAsync(string? text) =>
            !string.IsNullOrWhiteSpace(text) && (await MatcherAsync()).ContainsProhibited(text);

        public async Task<string> MaskAsync(string text) =>
            string.IsNullOrEmpty(text) ? text : (await MatcherAsync()).Mask(text);

        public void InvalidateCache() => _cache.Remove(CacheKey);

        private async Task<ModerationMatcher> MatcherAsync()
        {
            if (_cache.TryGetValue(CacheKey, out ModerationMatcher? cached) && cached != null)
                return cached;

            var rows = await _context.ModerationWords.AsNoTracking()
                .Select(w => new { w.Word, w.IsAllowed })
                .ToListAsync();
            var matcher = new ModerationMatcher(
                rows.Where(w => !w.IsAllowed).Select(w => w.Word),
                rows.Where(w => w.IsAllowed).Select(w => w.Word));
            _cache.Set(CacheKey, matcher);
            return matcher;
        }
    }
}
