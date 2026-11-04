using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EduConnect.Web.Services
{
    public class SmartTagService : ISmartTagService
    {
        public const string ProcessType = "SmartTag";
        public const string OutcomeProcessType = "SmartTagOutcome";

        // AISummary is NVARCHAR(255); same limit the chatbot uses.
        public const int MaxSummaryLength = 250;

        private readonly ApplicationDbContext _context;
        private readonly IAudienceService _audience;
        private readonly ISmartTagModel _model;
        private readonly ILogger<SmartTagService> _logger;
        private readonly IMemoryCache _cache;
        private readonly int _dailyLimit;
        private readonly int _maxInputChars;

        // Tests pin "now" so date handling is deterministic.
        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        // A per-author, per-day count of AI calls. Kept in memory: a restart
        // resets it, which is acceptable for a cost guard on one instance.
        private sealed class Counter
        {
            public int Value;
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public SmartTagService(ApplicationDbContext context, IAudienceService audience,
            ISmartTagModel model, ILogger<SmartTagService> logger,
            IMemoryCache cache, IConfiguration config)
        {
            _context = context;
            _audience = audience;
            _model = model;
            _logger = logger;
            _cache = cache;
            _dailyLimit = Math.Max(0, config.GetValue("SmartTag:DailyLimitPerUser", 20));
            _maxInputChars = Math.Max(200, config.GetValue("SmartTag:MaxInputChars", 4000));
        }

        public async Task<SmartTagSuggestion> SuggestAsync(int authorId, string title, string body)
        {
            var options = await GetOptionsAsync(authorId);
            var text = $"{title}\n\n{body}";
            if (text.Length > _maxInputChars)
                text = text[.._maxInputChars];

            // Reserve one AI call before making it, so quick repeat clicks
            // can't slip past the limit.
            var today = Now().Date;
            var counter = _cache.GetOrCreate($"smarttag:{authorId}:{today:yyyyMMdd}", entry =>
            {
                // Relative, so it agrees with Now() (the key carries the date).
                entry.AbsoluteExpirationRelativeToNow = today.AddDays(1) - Now();
                return new Counter();
            })!;
            var used = Interlocked.Increment(ref counter.Value);
            var allowed = used <= _dailyLimit;

            SmartTagSuggestion? suggestion = null;
            int? tokens = null;
            if (allowed)
            {
                var reply = await _model.AskAsync(BuildSystemPrompt(options, Now()), text);
                if (reply != null)
                {
                    suggestion = ParseModelReply(reply.Json, options, Now());
                    tokens = reply.TokensUsed;
                }
            }
            suggestion ??= SuggestByRules(text, options, Now());
            suggestion.DailyLimit = _dailyLimit;
            suggestion.AiRemaining = Math.Max(0, _dailyLimit - used);
            suggestion.LimitReached = !allowed;

            var log = new AIProcessingLog
            {
                ProcessType = ProcessType,
                InputText = text,
                OutputText = JsonSerializer.Serialize(suggestion, Json),
                ModelUsed = suggestion.Source == "ai" ? _model.Name : "rules",
                TokensUsed = tokens,
                ProcessedAt = Now()
            };
            try
            {
                _context.AIProcessingLogs.Add(log);
                await _context.SaveChangesAsync();
                suggestion.LogID = log.AILogID;
            }
            catch (Exception ex)
            {
                // A failed log write must not cost the poster their suggestions.
                _logger.LogWarning(ex, "Could not log smart tag run");
                _context.Entry(log).State = EntityState.Detached;
            }
            return suggestion;
        }

        // ─── What the author may pick ────────────
        public async Task<SmartTagOptions> GetOptionsAsync(int authorId)
        {
            var options = new SmartTagOptions();

            var categories = await _context.AnnouncementCategories
                .Where(c => c.IsActive && !c.IsEmergency && c.CategoryName != "Emergency")
                .OrderBy(c => c.CategoryID)
                .ToListAsync();
            options.Categories = categories.Select(c => new SmartTagChoice(c.CategoryID, c.CategoryName, null)).ToList();
            options.CategoryDescriptions = categories.ToDictionary(c => c.CategoryID, c => c.Description ?? "");

            var targets = await _audience.GetTargetOptionsAsync(authorId);
            if (targets.College != null)
            {
                var college = targets.College;
                if (targets.CanTargetCollege)
                    options.Colleges.Add(new SmartTagChoice(college.CollegeID, college.Name, college.ShortName));
                foreach (var dept in college.Departments)
                {
                    if (targets.CanTargetDepartments && !dept.IsImplicit)
                        options.Departments.Add(new SmartTagChoice(dept.DepartmentID, dept.Name, dept.ShortName));
                    options.Programs.AddRange(dept.Programs.Select(p =>
                        new SmartTagChoice(p.ProgramID, p.Name, p.ShortName, dept.DepartmentID)));
                }
            }

            // Same rule as the form's tag list (AnnouncementController.GetSelectableTagsAsync).
            var tagIDs = await _context.UserDepartments
                .Where(ud => ud.UserID == authorId)
                .Select(ud => ud.TagID)
                .ToListAsync();
            options.Tags = await _context.DepartmentTags
                .Where(d => d.IsActive && tagIDs.Contains(d.TagID) &&
                            !_context.Colleges.Any(c => c.LegacyTagID == d.TagID))
                .OrderBy(d => d.TagName)
                .Select(d => new SmartTagChoice(d.TagID, d.TagName, d.ShortName, null))
                .ToListAsync();

            return options;
        }

        // ─── Gemini ──────────────────────────────
        public static string BuildSystemPrompt(SmartTagOptions options, DateTime now)
        {
            static string List(IEnumerable<SmartTagChoice> items) =>
                items.Any()
                    ? string.Join("\n", items.Select(i =>
                        $"  - id {i.ID}: {i.Name}{(string.IsNullOrWhiteSpace(i.ShortName) ? "" : $" ({i.ShortName})")}"))
                    : "  (none)";

            var categories = string.Join("\n", options.Categories.Select(c =>
                $"  - id {c.ID}: {c.Name}" +
                (options.CategoryDescriptions.TryGetValue(c.ID, out var d) && d != "" ? $" — {d}" : "")));

            return
                "You tag announcements for Adamson University's campus communication system. " +
                "Read the announcement the user gives you and fill in its settings. " +
                "Choose ONLY from the ids listed below; never invent an id.\n\n" +
                $"Today is {now:dddd, MMMM d, yyyy h:mm tt} (Philippine time).\n\n" +
                $"Categories (pick exactly one):\n{categories}\n\n" +
                "Priority: 1 = Low (general information), 2 = Medium (has a deadline or required action), " +
                "3 = High (urgent, mandatory, or happening within a day).\n\n" +
                "Audience — pick the NARROWEST groups the text is clearly meant for. " +
                "A program is narrower than a department, a department narrower than the college. " +
                "Use School Wide only when the text is clearly for the whole university. " +
                "If the text names no audience, return empty lists.\n" +
                $"Colleges:\n{List(options.Colleges)}\n" +
                $"Departments:\n{List(options.Departments)}\n" +
                $"Programs:\n{List(options.Programs)}\n" +
                $"Tags:\n{List(options.Tags)}\n\n" +
                "Expiry: the moment the announcement stops being useful (its deadline or event end), " +
                "as \"yyyy-MM-ddTHH:mm\" Philippine time. Use 23:59 when only a date is given. " +
                "null when the text has no date.\n\n" +
                $"Summary: one plain-sentence summary under {MaxSummaryLength} characters keeping dates, " +
                "venues and required actions. No title, bullets or markdown.\n\n" +
                "Each reason is one short sentence quoting or pointing at the words in the text that led to the choice.\n\n" +
                "Reply with JSON only, in exactly this shape:\n" +
                "{\"categoryId\": 1, \"categoryReason\": \"\", \"priority\": 1, \"priorityReason\": \"\", " +
                "\"expiresAt\": null, \"expiryReason\": \"\", \"summary\": \"\", " +
                "\"collegeIds\": [], \"departmentIds\": [], \"programIds\": [], \"tagIds\": [], \"audienceReason\": \"\"}";
        }

        private sealed class ModelReply
        {
            public int? CategoryId { get; set; }
            public string? CategoryReason { get; set; }
            public int? Priority { get; set; }
            public string? PriorityReason { get; set; }
            public string? ExpiresAt { get; set; }
            public string? ExpiryReason { get; set; }
            public string? Summary { get; set; }
            public List<int>? CollegeIds { get; set; }
            public List<int>? DepartmentIds { get; set; }
            public List<int>? ProgramIds { get; set; }
            public List<int>? TagIds { get; set; }
            public string? AudienceReason { get; set; }
        }

        // Keeps only what the author may pick; anything else the model
        // returned is dropped. Null when the reply isn't usable JSON.
        public static SmartTagSuggestion? ParseModelReply(string json, SmartTagOptions options, DateTime now)
        {
            ModelReply? reply;
            try
            {
                reply = JsonSerializer.Deserialize<ModelReply>(StripFence(json), Json);
            }
            catch (JsonException)
            {
                return null;
            }
            if (reply == null) return null;

            static List<int> Allowed(List<int>? ids, List<SmartTagChoice> choices) =>
                (ids ?? new()).Where(id => choices.Any(c => c.ID == id)).Distinct().ToList();

            var s = new SmartTagSuggestion { Source = "ai" };

            if (reply.CategoryId is int cat && options.Categories.Any(c => c.ID == cat))
            {
                s.CategoryID = cat;
                s.CategoryReason = Clean(reply.CategoryReason);
            }

            if (reply.Priority is >= 1 and <= 3)
            {
                s.Priority = (byte)reply.Priority.Value;
                s.PriorityReason = Clean(reply.PriorityReason);
            }

            if (DateTime.TryParse(reply.ExpiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var expires) &&
                expires > now && expires < now.AddYears(1))
            {
                s.ExpiresAt = expires;
                s.ExpiryReason = Clean(reply.ExpiryReason);
            }

            var summary = Clean(reply.Summary);
            if (summary != null && summary.Length > MaxSummaryLength)
                summary = summary[..MaxSummaryLength].TrimEnd() + "…";
            s.Summary = summary;

            s.CollegeIDs = Allowed(reply.CollegeIds, options.Colleges);
            s.DepartmentIDs = Allowed(reply.DepartmentIds, options.Departments);
            s.ProgramIDs = Allowed(reply.ProgramIds, options.Programs);
            s.TagIDs = Allowed(reply.TagIds, options.Tags);
            Narrow(s, options);
            if (s.HasAudience)
                s.AudienceReason = Clean(reply.AudienceReason);

            return s;
        }

        // Keeps the narrowest picks: a department goes when one of its
        // programs is picked, and the college goes when anything inside it
        // is (the author's options only ever hold their own college).
        // Ticking both would reach the whole broader group.
        public static void Narrow(SmartTagSuggestion s, SmartTagOptions options)
        {
            var programDepts = options.Programs
                .Where(p => s.ProgramIDs.Contains(p.ID) && p.ParentID != null)
                .Select(p => p.ParentID!.Value)
                .ToHashSet();
            s.DepartmentIDs.RemoveAll(programDepts.Contains);
            if (s.DepartmentIDs.Count > 0 || s.ProgramIDs.Count > 0)
                s.CollegeIDs.Clear();
        }

        private static string StripFence(string json)
        {
            var t = json.Trim();
            if (!t.StartsWith("```")) return t;
            t = t[(t.IndexOf('\n') + 1)..];
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            return end >= 0 ? t[..end] : t;
        }

        private static string? Clean(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // ─── Keyword fallback ────────────────────
        private static readonly string[] SchoolWidePhrases =
        {
            "all students", "everyone", "school wide", "school-wide", "schoolwide",
            "campus wide", "campus-wide", "university wide", "university-wide", "all adamsonians"
        };

        private static readonly string[] HighWords =
            { "urgent", "immediately", "mandatory", "asap", "today", "tonight", "suspended", "cancelled", "canceled" };

        private static readonly string[] MediumWords =
            { "deadline", "required", "reminder", "important", "must", "until", "due", "submit", "register" };

        private static readonly Regex DatePattern = new(
            @"\b(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static SmartTagSuggestion SuggestByRules(string text, SmartTagOptions options, DateTime now)
        {
            var s = new SmartTagSuggestion { Source = "rules" };
            var lower = text.ToLowerInvariant();
            var words = Regex.Matches(lower, @"[a-z0-9&]+").Select(m => m.Value).ToHashSet();

            // Audience: names (any case) or short names (as written) that
            // appear as whole words.
            bool Mentions(SmartTagChoice c, out string hit)
            {
                hit = "";
                if (c.Name.Length >= 4 && Regex.IsMatch(text, $@"\b{Regex.Escape(c.Name)}\b", RegexOptions.IgnoreCase))
                {
                    hit = c.Name;
                    return true;
                }
                if (!string.IsNullOrWhiteSpace(c.ShortName) && c.ShortName.Length >= 2 &&
                    Regex.IsMatch(text, $@"(?<![A-Za-z0-9]){Regex.Escape(c.ShortName)}(?![A-Za-z0-9])"))
                {
                    hit = c.ShortName;
                    return true;
                }
                return false;
            }

            var hits = new List<string>();
            void Collect(List<SmartTagChoice> choices, List<int> into)
            {
                foreach (var c in choices)
                    if (Mentions(c, out var hit))
                    {
                        into.Add(c.ID);
                        hits.Add(hit);
                    }
            }
            Collect(options.Programs, s.ProgramIDs);
            Collect(options.Departments, s.DepartmentIDs);
            Collect(options.Colleges, s.CollegeIDs);
            Narrow(s, options);

            var schoolWide = options.Tags.FirstOrDefault(t => t.ShortName == AudienceService.SchoolWide);
            var phrase = SchoolWidePhrases.FirstOrDefault(lower.Contains);
            if (schoolWide != null && phrase != null && s.ProgramIDs.Count + s.DepartmentIDs.Count + s.CollegeIDs.Count == 0)
            {
                s.TagIDs.Add(schoolWide.ID);
                hits.Add(phrase);
            }
            if (s.HasAudience)
                s.AudienceReason = $"Mentions {string.Join(", ", hits.Distinct().Select(h => $"\"{h}\""))}.";

            // Category: most description/name words found in the text.
            static string Stem(string w) => w.Length > 4 && w.EndsWith("s") ? w[..^1] : w;
            var stems = words.Select(Stem).ToHashSet();
            var best = options.Categories
                .Select(c =>
                {
                    var keys = Regex.Matches(
                            $"{c.Name} {options.CategoryDescriptions.GetValueOrDefault(c.ID)}".ToLowerInvariant(),
                            @"[a-z]{4,}")
                        .Select(m => Stem(m.Value))
                        .Where(k => k is not "general" and not "campus" and not "information" and not "update")
                        .Distinct()
                        .ToList();
                    var found = keys.Where(stems.Contains).ToList();
                    return (Choice: c, Found: found);
                })
                .Where(x => x.Found.Count > 0)
                .OrderByDescending(x => x.Found.Count)
                .FirstOrDefault();
            if (best.Choice != null)
            {
                s.CategoryID = best.Choice.ID;
                s.CategoryReason = $"Mentions {string.Join(", ", best.Found.Select(f => $"\"{f}\""))}.";
            }
            else if (options.Categories.FirstOrDefault(c => c.Name == "General") is { } general)
            {
                s.CategoryID = general.ID;
                s.CategoryReason = "No category keywords found.";
            }

            // Priority from wording.
            var high = HighWords.FirstOrDefault(words.Contains);
            var medium = MediumWords.FirstOrDefault(words.Contains);
            (s.Priority, s.PriorityReason) = high != null ? ((byte)3, $"Says \"{high}\".")
                : medium != null ? ((byte)2, $"Says \"{medium}\".")
                : ((byte)1, "No urgent or deadline wording.");

            // Expiry: the latest future date written as "Month day[, year]".
            DateTime? latest = null;
            string? latestText = null;
            foreach (Match m in DatePattern.Matches(text))
            {
                var month = DateTime.ParseExact(m.Groups[1].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
                var day = int.Parse(m.Groups[2].Value);
                var year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : now.Year;
                if (day < 1 || day > DateTime.DaysInMonth(year, month)) continue;
                var date = new DateTime(year, month, day, 23, 59, 0);
                if (!m.Groups[3].Success && date < now) date = date.AddYears(1);
                if (date <= now || date > now.AddYears(1)) continue;
                if (latest == null || date > latest)
                {
                    latest = date;
                    latestText = m.Value;
                }
            }
            if (latest != null)
            {
                s.ExpiresAt = latest;
                s.ExpiryReason = $"Latest date mentioned: \"{latestText}\".";
            }

            return s;
        }

        // ─── What the poster kept ────────────────
        public async Task RecordOutcomeAsync(int logId, int announcementId, SmartTagFinal final)
        {
            try
            {
                // Only an unlinked run (or one already linked to this
                // announcement) from the last day counts.
                var since = Now().AddDays(-1);
                var log = await _context.AIProcessingLogs.FirstOrDefaultAsync(l =>
                    l.AILogID == logId && l.ProcessType == ProcessType && l.ProcessedAt >= since &&
                    (l.AnnouncementID == null || l.AnnouncementID == announcementId));
                if (log?.OutputText == null) return;

                var suggested = JsonSerializer.Deserialize<SmartTagSuggestion>(log.OutputText, Json);
                if (suggested == null) return;

                var kept = CompareKept(suggested, final);
                log.AnnouncementID = announcementId;
                _context.AIProcessingLogs.Add(new AIProcessingLog
                {
                    AnnouncementID = announcementId,
                    ProcessType = OutcomeProcessType,
                    InputText = $"SmartTag log {logId}",
                    OutputText = JsonSerializer.Serialize(new { suggestionLogId = logId, kept }, Json),
                    ModelUsed = log.ModelUsed,
                    Confidence = kept.Count == 0 ? null
                        : Math.Round((decimal)kept.Count(k => k.Value) / kept.Count, 4),
                    ProcessedAt = Now()
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The announcement is already saved; losing the stat is fine.
                _logger.LogWarning(ex, "Could not record smart tag outcome for log {LogId}", logId);
            }
        }

        // Field → whether the saved value matches the suggestion, for the
        // fields the run actually suggested.
        public static Dictionary<string, bool> CompareKept(SmartTagSuggestion s, SmartTagFinal f)
        {
            static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a.ToHashSet().SetEquals(b);

            var kept = new Dictionary<string, bool>();
            if (s.CategoryID != null) kept["category"] = s.CategoryID == f.CategoryID;
            if (s.Priority != null) kept["priority"] = s.Priority == f.Priority;
            if (s.ExpiresAt != null)
                kept["expiry"] = f.ExpiresAt != null &&
                                 Math.Abs((f.ExpiresAt.Value - s.ExpiresAt.Value).TotalMinutes) < 1;
            if (s.Summary != null)
                kept["summary"] = string.Equals(s.Summary.Trim(), f.Summary?.Trim(), StringComparison.Ordinal);
            if (s.HasAudience)
                kept["audience"] = Same(s.CollegeIDs, f.CollegeIDs) && Same(s.DepartmentIDs, f.DepartmentIDs) &&
                                   Same(s.ProgramIDs, f.ProgramIDs) && Same(s.TagIDs, f.TagIDs);
            return kept;
        }
    }
}
