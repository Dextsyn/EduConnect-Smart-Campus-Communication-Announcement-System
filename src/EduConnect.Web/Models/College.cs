using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Colleges")]
    public class College
    {
        [Key]
        public int CollegeID { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        // The flat DepartmentTags row this college replaces. Used to
        // backfill placement and announcement targets; NULL for colleges
        // that had no tag (College of Education & Liberal Arts).
        public int? LegacyTagID { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public DepartmentTag? LegacyTag { get; set; }
        public ICollection<Department> Departments { get; set; } = new List<Department>();
    }
}
