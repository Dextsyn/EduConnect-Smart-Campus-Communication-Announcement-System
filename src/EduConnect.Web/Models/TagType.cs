using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("TagTypes")]
    public class TagType
    {
        [Required]
        public int TagTypeID { get; set; }

        [Required]
        [MaxLength(50)]
        public string TypeName { get; set; }

        [Required]
        [MaxLength(50)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<DepartmentTag> DepartmentTags { get; set; }
    }
}
