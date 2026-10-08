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

    // The dashboard's Pending Announcements card: the oldest few rows and
    // how many there are in all. Rendered by _PendingAnnouncements, both
    // in the page and when a live update refreshes the card.
    public class PendingAnnouncementsViewModel
    {
        public List<PendingAnnouncementRow> Rows { get; set; } = new();
        public int Count { get; set; }
    }
}
