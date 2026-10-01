namespace EduConnect.Web.ViewModels
{
    // One of a faculty member's announcements that still needs someone to
    // act: a reviewer (pending), the author (rejected, or approved and
    // waiting to be published).
    public class FacultySubmissionRow
    {
        public int AnnouncementID { get; set; }
        public string Title { get; set; } = "";
        public string ApprovalStatus { get; set; } = "";
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }
}
