using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // What the poster may pick on the announcement form. Smart tagging
    // chooses only from these, so it can never widen an audience.
    // ParentID: a program's department (null for everything else).
    public record SmartTagChoice(int ID, string Name, string? ShortName, int? ParentID = null);

    public class SmartTagOptions
    {
        public List<SmartTagChoice> Categories { get; set; } = new();
        public List<SmartTagChoice> Colleges { get; set; } = new();
        public List<SmartTagChoice> Departments { get; set; } = new();
        public List<SmartTagChoice> Programs { get; set; } = new();
        public List<SmartTagChoice> Tags { get; set; } = new();

        // Category descriptions, for the keyword fallback.
        public Dictionary<int, string> CategoryDescriptions { get; set; } = new();
    }

    // One Smart tag run. Empty lists / nulls mean "no suggestion": the form
    // keeps what the poster already has for that field.
    public class SmartTagSuggestion
    {
        public string Source { get; set; } = "ai";    // "ai" or "rules"
        public int? LogID { get; set; }

        // The author's daily AI allowance (SmartTag:DailyLimitPerUser).
        // Past it, keyword rules answer instead and no tokens are spent.
        public int DailyLimit { get; set; }
        public int AiRemaining { get; set; }
        public bool LimitReached { get; set; }

        public int? CategoryID { get; set; }
        public string? CategoryReason { get; set; }

        public byte? Priority { get; set; }
        public string? PriorityReason { get; set; }

        public DateTime? ExpiresAt { get; set; }
        public string? ExpiryReason { get; set; }

        public string? Summary { get; set; }

        public List<int> CollegeIDs { get; set; } = new();
        public List<int> DepartmentIDs { get; set; } = new();
        public List<int> ProgramIDs { get; set; } = new();
        public List<int> TagIDs { get; set; } = new();
        public string? AudienceReason { get; set; }

        public bool HasAudience =>
            CollegeIDs.Count + DepartmentIDs.Count + ProgramIDs.Count + TagIDs.Count > 0;
    }

    // What the poster finally saved, compared with the suggestion.
    public record SmartTagFinal(
        int CategoryID,
        byte Priority,
        DateTime? ExpiresAt,
        string? Summary,
        IReadOnlyList<int> CollegeIDs,
        IReadOnlyList<int> DepartmentIDs,
        IReadOnlyList<int> ProgramIDs,
        IReadOnlyList<int> TagIDs);

    public interface ISmartTagService
    {
        // Reads the title and body and picks category, priority, expiry,
        // summary and audience from what the author may choose. Gemini
        // first; keyword rules when it is unavailable. Logged in
        // AIProcessingLogs ("SmartTag").
        Task<SmartTagSuggestion> SuggestAsync(int authorId, string title, string body);

        // Links a run to the saved announcement and logs which suggestions
        // the poster kept ("SmartTagOutcome"; Confidence = share kept).
        Task RecordOutcomeAsync(int logId, int announcementId, SmartTagFinal final);
    }

    // The language model behind smart tagging, so tests can stand in for
    // Gemini. Returns the raw JSON reply, or null when it can't answer.
    public interface ISmartTagModel
    {
        string Name { get; }
        Task<SmartTagModelReply?> AskAsync(string systemPrompt, string userText);
    }

    public record SmartTagModelReply(string Json, int? TokensUsed);
}
