using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    // Fills the moderation list at startup from a private setting. The
    // GitHub repo is public, so the starting words never live in source:
    // locally they sit in the gitignored appsettings.Development.json, in
    // production in the App Service setting Moderation__SeedWords.
    public static class ModerationWordImporter
    {
        public const string SettingKey = "Moderation:SeedWords";

        // Runs only while the list has no prohibited words, so a word an
        // admin removed is not brought back by the next restart. Returns
        // the number of rows added.
        public static async Task<int> ImportAsync(ApplicationDbContext db,
            IConfiguration config, ILogger logger)
        {
            var setting = config[SettingKey];
            if (string.IsNullOrWhiteSpace(setting))
            {
                logger.LogWarning("Moderation:SeedWords is not set; no moderation words were imported.");
                return 0;
            }

            if (await db.ModerationWords.AnyAsync(w => !w.IsAllowed)) return 0;

            var existing = (await db.ModerationWords.Select(w => w.Word).ToListAsync()).ToHashSet();
            var words = setting.Split(new[] { '\r', '\n', ',' })
                .Select(ModerationMatcher.NormalizeEntry)
                .Where(w => w.Length > 0 && w.Length <= 100 && !existing.Contains(w))
                .Distinct()
                .ToList();

            db.ModerationWords.AddRange(words.Select(w => new ModerationWord { Word = w }));
            await db.SaveChangesAsync();
            return words.Count;
        }
    }
}
