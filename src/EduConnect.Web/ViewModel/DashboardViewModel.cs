namespace EduConnect.Web.ViewModels
{
    public class DashboardViewModel
    {
        // ─── Stat Cards ────────────────────────
        public int TotalAnnouncements { get; set; }
        public int TodayAnnouncements { get; set; }
        public int UnreadNotifications { get; set; }
        public int TotalUsers { get; set; }
        public int UpcomingEvents { get; set; }
        public int PendingFeedback { get; set; }
        public int MyAnnouncements { get; set; }
        public int TotalViews { get; set; }

        // ─── Upcoming Events (Student dashboard) ─
        public List<UpcomingEventItem> UpcomingEventsList { get; set; }
            = new List<UpcomingEventItem>();

        // ─── Table Data ─────────────────────────
        public List<AnnouncementTableViewModel>
            RecentAnnouncements
        { get; set; }
            = new List<AnnouncementTableViewModel>();

        // ─── Personalized Feed (Student only) ───
        public List<AnnouncementTableViewModel>
            DepartmentAnnouncements
        { get; set; }
            = new List<AnnouncementTableViewModel>();

        public List<AnnouncementTableViewModel>
            ForYouAnnouncements
        { get; set; }
            = new List<AnnouncementTableViewModel>();

        public List<AnnouncementTableViewModel>
            ExploreAnnouncements
        { get; set; }
            = new List<AnnouncementTableViewModel>();

        // ─── Search ─────────────────────────────
        public string? SearchQuery { get; set; }
        public string? FilterCategory { get; set; }
        public string? FilterFeedType { get; set; }
    }

    public class UpcomingEventItem
    {
        public int EventID { get; set; }
        public string EventTitle { get; set; } = "";
        public DateTime StartDateTime { get; set; }
        public string Location { get; set; } = "";
        public bool IsOnline { get; set; }
        public string? MeetingURL { get; set; }
    }

    public class AnnouncementTableViewModel
    {
        public int AnnouncementID { get; set; }
        public int AuthorID { get; set; }
        public string Title { get; set; }
        public string CategoryName { get; set; }
        public string CategoryColor { get; set; }
        public string FeedType { get; set; }
        public string AuthorName { get; set; }
        public string Status { get; set; }
        public int ViewCount { get; set; }
        public DateTime? PublishedAt { get; set; }
        public List<string> Tags { get; set; }
            = new List<string>();
    }
}