using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Groups")]
    public class Group
    {
        [Key]
        public int GroupID { get; set; }

        [Required]
        public int CreatorID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; }

        [Required]
        public int MaxMembers { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Open";
        // Values: Open | Full | Dissolved

        public DateTime? ChatExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User Creator { get; set; }
        public ICollection<GroupMember> Members { get; set; }
        public ICollection<GroupMessage> Messages { get; set; }
    }
}
