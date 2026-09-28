using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // Not "Program": that name is taken by the top-level-statements class
    // in Program.cs, which would win name lookup inside this project.
    [Table("Programs")]
    public class AcademicProgram
    {
        [Key]
        public int ProgramID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public Department Department { get; set; } = null!;
    }
}
