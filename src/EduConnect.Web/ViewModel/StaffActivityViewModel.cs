using EduConnect.Web.Models;

namespace EduConnect.Web.ViewModels
{
    public class StaffActivityFilterViewModel
    {
        public const string Mine = "mine";
        public const string All = "all";

        public string? Scope { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Status { get; set; }
        public string? Building { get; set; }
        public int Page { get; set; } = 1;

        public bool IsAll => Scope == All;
    }

    public class StaffActivityViewModel
    {
        public const int PageSize = 50;

        public StaffActivityFilterViewModel Filter { get; set; } = new();

        // Reports that came in during the period, whoever is looking.
        public int ReceivedCount { get; set; }
        public int ResolvedCount { get; set; }
        public int DismissedCount { get; set; }
        public int ActionCount { get; set; }

        public List<IncidentReportActivity> Rows { get; set; } = new();
        public int TotalRows { get; set; }

        public int PageCount =>
            Math.Max(1, (TotalRows + PageSize - 1) / PageSize);
    }
}
