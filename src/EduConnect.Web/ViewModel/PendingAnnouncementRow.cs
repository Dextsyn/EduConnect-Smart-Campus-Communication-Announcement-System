namespace EduConnect.Web.ViewModels
{
    // One announcement awaiting the viewer's approval, as the Dean /
    // Chairperson dashboard lists it.
    public class PendingAnnouncementRow
    {
        public int AnnouncementID { get; set; }
        public string Title { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public string? CategoryName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public List<string> Audience { get; set; } = new();
    }
}
