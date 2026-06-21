using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Organizations")]
    public class Organization
    {
        [Key]
        public int OrgID { get; set; }

        [Required]
        [MaxLength(200)]
        public string OrgName { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? LogoURL { get; set; }

        [MaxLength(500)]
        public string? CoverPhotoURL { get; set; }

        public int? DepartmentTagID { get; set; }
        // NULL = university wide organization

        [Required]
        public int CreatedByID { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsVerified { get; set; } = false;

        public DateTime CreatedAt { get; set; }
            = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public DepartmentTag? DepartmentTag { get; set; }
        public User CreatedBy { get; set; }
        public ICollection<OrgMember> Members { get; set; }
        public ICollection<OrgAnnouncement>
            Announcements
        { get; set; }
    }
}