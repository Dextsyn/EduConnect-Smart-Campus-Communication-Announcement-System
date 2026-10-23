using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Events")]
    public class Event
    {
        [Key]
        public int EventID { get; set; }

        public int? AnnouncementID { get; set; }

        [Required]
        public int OrganizerID { get; set; }

        [Required]
        [MaxLength(300)]
        public string EventTitle { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        [MaxLength(500)]
        public string? CoverPhotoURL { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; }

        [Required]
        public DateTime EndDateTime { get; set; }

        public int? MaxAttendees { get; set; }

        public int CurrentAttendees { get; set; }
            = 0;

        public bool IsOnline { get; set; } = false;

        [MaxLength(500)]
        public string? MeetingURL { get; set; }

        [MaxLength(20)]
        public string Status { get; set; }
            = "Upcoming";
        // Values: Upcoming, Ongoing,
        //         Completed, Cancelled

        public bool IsRegistrationOpen { get; set; }
            = true;

        public DateTime? RegistrationDeadline
        { get; set; }

        // Who the event is for: the organizer's college and department
        // when it was created (stamped by the server, never moved with
        // the organizer). DepartmentID is null for a Dean's event.
        // IsDepartmentOnly narrows it from the college to the department.
        // See EventAudience.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public bool IsDepartmentOnly { get; set; }

        public DateTime CreatedAt { get; set; }
            = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Announcement? Announcement
        { get; set; }
        public User Organizer { get; set; }
        public College? College { get; set; }
        public Department? Department { get; set; }
        public ICollection<EventRegistration>
            Registrations
        { get; set; }
        public ICollection<EventWaitlist>
            Waitlist
        { get; set; }
    }
}