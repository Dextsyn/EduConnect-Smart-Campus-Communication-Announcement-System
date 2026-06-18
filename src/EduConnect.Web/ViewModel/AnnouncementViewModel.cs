using EduConnect.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    // ─── For Creating/Editing Announcements ──
    public class AnnouncementFormViewModel
    {
        public int AnnouncementID { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [MaxLength(150)]
        public string Title { get; set; }

        [Required(ErrorMessage = "Body is required")]
        public string Body { get; set; }

        [Required(ErrorMessage = "Category is required")]
        public int CategoryID { get; set; }

        [Required(ErrorMessage = "Priority is required")]
        public byte Priority { get; set; } = 1;

        public List<int> SelectedTagIDs { get; set; }
            = new List<int>();

        public DateTime? ExpiresAt { get; set; }
        public bool IsEmergency { get; set; } = false;

        // Optional photo
        public IFormFile? Photo { get; set; }
        public string? ExistingPhotoURL { get; set; }
        public bool RemovePhoto { get; set; } = false;

        // Dropdown data
        public List<AnnouncementCategory> Categories { get; set; }
            = new List<AnnouncementCategory>();
        public List<DepartmentTag> AvailableTags { get; set; }
            = new List<DepartmentTag>();
    }

    // ─── For Displaying Announcement Details ─
    public class AnnouncementDetailViewModel
    {
        public int AnnouncementID { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string? AISummary { get; set; }
        public string FeedType { get; set; }
        public string Status { get; set; }
        public byte Priority { get; set; }
        public bool IsEmergency { get; set; }
        public int ViewCount { get; set; }
        public string? AttachmentURL { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Related data
        public string AuthorName { get; set; }
        public string AuthorRole { get; set; }
        public string CategoryName { get; set; }
        public string CategoryColor { get; set; }
        public List<string> Tags { get; set; }
            = new List<string>();

        // Feedback
        public double AverageRating { get; set; }
        public int TotalFeedback { get; set; }
        public bool UserHasRated { get; set; }
        public List<FeedbackItemViewModel> Feedbacks { get; set; }
            = new List<FeedbackItemViewModel>();
    }

    // ─── For Feedback Items ───────────────────
    public class FeedbackItemViewModel
    {
        public int FeedbackID { get; set; }
        public string UserName { get; set; }
        public string? FeedbackText { get; set; }
        public byte? Rating { get; set; }
        public string? SentimentLabel { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ─── For Submitting Feedback ──────────────
    public class FeedbackFormViewModel
    {
        [Required]
        public int AnnouncementID { get; set; }

        [Range(1, 5, ErrorMessage =
            "Please select a rating")]
        public byte Rating { get; set; }

        [MaxLength(1000)]
        public string? FeedbackText { get; set; }
    }
}