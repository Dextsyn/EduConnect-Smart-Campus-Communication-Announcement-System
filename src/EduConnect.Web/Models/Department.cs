using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Departments")]
    public class Department
    {
        [Key]
        public int DepartmentID { get; set; }

        [Required]
        public int CollegeID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        // Colleges without departments (Architecture, Law, Nursing,
        // Pharmacy) get exactly one implicit department so every program
        // has a department. The UI never shows implicit departments.
        public bool IsImplicit { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public College College { get; set; } = null!;
        public ICollection<AcademicProgram> Programs { get; set; } = new List<AcademicProgram>();
    }
}
