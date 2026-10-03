namespace EduConnect.Web.ViewModels
{
    // /Admin/AuditLog: filters in, one page of rows out.
    public class AuditLogViewModel
    {
        public const int PageSize = 50;

        public string? Actor { get; set; }
        public string? Area { get; set; }
        public string? ActionName { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int Page { get; set; } = 1;

        public int TotalCount { get; set; }
        public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

        public List<string> Actions { get; set; } = new();
        public List<AuditLogRow> Rows { get; set; } = new();
    }

    public class AuditLogRow
    {
        public DateTime CreatedAt { get; set; }
        public string Actor { get; set; } = "";
        public bool ActorDeleted { get; set; }
        public string Action { get; set; } = "";
        public string Area { get; set; } = "";
        public string? Summary { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? IPAddress { get; set; }
    }
}
