using EduConnect.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    // ─── For Creating Events ───────────────
    public class EventFormViewModel
    {
        public int EventID { get; set; }

        [Required(ErrorMessage =
            "Event title is required")]
        [MaxLength(300)]
        public string EventTitle { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        [Required(ErrorMessage =
        "Start date is required")]
        public DateTime? StartDateTime { get; set; }

        [Required(ErrorMessage =
            "End date is required")]
        public DateTime? EndDateTime { get; set; }

        public int? MaxAttendees { get; set; }

        public bool IsOnline { get; set; } = false;

        [MaxLength(500)]
        public string? MeetingURL { get; set; }

        public DateTime? RegistrationDeadline
        { get; set; }

        public int? LinkedAnnouncementID { get; set; }

        public IFormFile? CoverPhoto { get; set; }
        public string? ExistingCoverPhotoURL
        { get; set; }
        public bool RemoveCoverPhoto { get; set; }

        // Dropdown data
        public List<Announcement> Announcements
        { get; set; }
            = new List<Announcement>();
    }

    // ─── For Displaying Event Details ──────
    public class EventDetailViewModel
    {
        public int EventID { get; set; }
        public string EventTitle { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? CoverPhotoURL { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public int? MaxAttendees { get; set; }
        public int CurrentAttendees { get; set; }
        public bool IsOnline { get; set; }
        public string? MeetingURL { get; set; }
        public string Status { get; set; }
        public bool IsRegistrationOpen { get; set; }
        public DateTime? RegistrationDeadline
        { get; set; }
        public DateTime CreatedAt { get; set; }

        // Organizer info
        public string OrganizerName { get; set; }
        public string OrganizerRole { get; set; }

        // Registration status for current user
        public bool IsRegistered { get; set; }
        public bool IsOnWaitlist { get; set; }
        public bool IsFull { get; set; }
        public int SlotsRemaining { get; set; }
        public int WaitlistPosition { get; set; }
        public string? UserQRCode { get; set; }

        // Registration status string
        public string RegistrationStatus { get; set; }
            = "Open";
        // Values: Open, Full, Waitlist,
        //         Registered, Closed, Cancelled

        // Linked announcement
        public string? AnnouncementTitle { get; set; }
        public int? AnnouncementID { get; set; }

        // Registrations list (for organizer)
        public List<EventRegistrationViewModel>
            Registrations
        { get; set; }
            = new List<EventRegistrationViewModel>();
    }

    // ─── For Registration List ─────────────
    public class EventRegistrationViewModel
    {
        public int RegistrationID { get; set; }
        public string StudentName { get; set; }
        public string StudentID { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public string Status { get; set; }
        public string? QRCode { get; set; }
        public DateTime RegisteredAt { get; set; }
    }

    // ─── For Event List/Calendar ───────────
    public class EventListViewModel
    {
        public int EventID { get; set; }
        public string EventTitle { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? CoverPhotoURL { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public int? MaxAttendees { get; set; }
        public int CurrentAttendees { get; set; }
        public bool IsOnline { get; set; }
        public string Status { get; set; }
        public bool IsRegistrationOpen { get; set; }
        public bool IsRegistered { get; set; }
        public bool IsFull { get; set; }
        public int SlotsRemaining { get; set; }
        public string OrganizerName { get; set; }
        public int OrganizerID { get; set; }
    }

    // ─── For Dedicated Registrants Page ───
    public class EventRegistrantsViewModel
    {
        public int EventID { get; set; }
        public string EventTitle { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string? Location { get; set; }
        public string OrganizerName { get; set; }

        public int RegisteredCount { get; set; }
        public int AttendedCount { get; set; }
        public int CancelledCount { get; set; }
        public int WaitlistedCount { get; set; }

        public List<EventRegistrationViewModel> Registrations { get; set; } = new();
        public List<WaitlistEntryViewModel> Waitlist { get; set; } = new();
    }

    // ─── For Waitlist Row in Registrants Page
    public class WaitlistEntryViewModel
    {
        public int WaitlistID { get; set; }
        public int Position { get; set; }
        public string StudentName { get; set; }
        public string StudentID { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public string Status { get; set; }
        public DateTime JoinedAt { get; set; }
    }

    // ─── For QR Scan Landing Page ──────────
    public class ScanResultViewModel
    {
        public int RegistrationID { get; set; }
        public string RegistrationStatus { get; set; }
        public DateTime RegisteredAt { get; set; }

        public string StudentFullName { get; set; }
        public string StudentID { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }

        public int EventID { get; set; }
        public string EventTitle { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string? Location { get; set; }
    }
}