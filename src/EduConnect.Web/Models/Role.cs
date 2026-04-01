using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Roles")]
    public class Role
    {
        [Key]
        public int RoleID { get; set; }
        [Required]
        [MaxLength(50)]
        public string RoleName { get; set; }

        [Required]
        public int RoleLevel { get; set; }
        [MaxLength(255)]
        public string? Description { get; set; }

        [Required]
        public bool CanPublish { get; set; } = false;

        [Required]
        public bool CanManageUsers { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public ICollection<User> Users { get; set; }


    }
}
