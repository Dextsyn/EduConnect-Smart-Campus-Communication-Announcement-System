using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.AccessControl;

namespace EduConnect.Web.Models
{
    [Table("Users")]
    public class User
    {
        [Required]
        public int UserID { get; set; }
           
        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; }

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; }

        [Required]
        [MaxLength(100)]
        public string Email { get; set; }

        [Required]
        [MaxLength(512)]
        public string PasswordHash { get; set; }

        [MaxLength(50)]
        public string? StudentID { get; set; }

        [Required]
        public int RoleID { get; set; }
        public bool IsActive { get; set; } = true;
        public string VerificationStatus { get; set; } = "Pending";

        [MaxLength(500)]
        public string? ProfilePicture { get; set; }

        [MaxLength(10)]
        public string? Suffix { get; set; }

        // Placement in the academic hierarchy. Stored at every level so
        // feed and routing queries filter on one column; Plan 2's
        // placement service is the only writer and keeps them consistent.
        // Dean: College. Chairperson/Faculty: Department (+College).
        // Student: Program (+Department, +College). Others: none.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        public int? VerifiedByID { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? VerificationRejectionReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }

        public Role Role { get; set; }
        public User? VerifiedBy { get; set; }
        public College? College { get; set; }
        public Department? Department { get; set; }
        public AcademicProgram? AcademicProgram { get; set; }
        public ICollection<UserDepartment> UserDepartments { get; set; }    
        public ICollection<Announcement> Announcements { get; set; }
        public ICollection<Notification> Notifications { get; set; }
        public ICollection<Feedback> Feedbacks { get; set; }
        public ICollection<ChatbotConversation> ChatbotConversations { get; set; }
        public ICollection<RefreshToken> RefreshTokens { get; set; }
        public ICollection<AuditLog> AuditLogs { get; set; }
        public ICollection<Event> OrganizedEvents { get; set; }
        public ICollection<UserAnnouncementInteraction> AnnouncementInteractions { get; set; }

    }
}
