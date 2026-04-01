using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("UserDepartments")]
    public class UserDepartment
    {
        [Key]
        public int UserDepartmentID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        public int TagID { get; set; }

        public bool IsPrimary { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public User User { get; set; }
        public DepartmentTag DepartmentTag { get; set; }
    }
}