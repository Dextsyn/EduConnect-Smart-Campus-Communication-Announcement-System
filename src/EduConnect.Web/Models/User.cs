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

        [MaxLength(500)]
        public string? ProfilePicture { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }

        public Role Role { get; set; }  
        public ICollection<UserDepartment> UserDepartments { get; set; }    
        public ICollection<Announcement> Announcements { get; set; }
        public ICollection<Notification> Notifications { get; set; }
        public ICollection<Feedback> Feedbacks { get; set; }
        public ICollection<ChatbotConversation> ChatbotConversations { get; set; }
        public ICollection<RefreshToken> RefreshTokens { get; set; }
        public ICollection<AuditLog> AuditLogs { get; set; }
        public ICollection<Event> OrganizedEvents { get; set; }

    }
}
