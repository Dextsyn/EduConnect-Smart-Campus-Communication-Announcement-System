using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("DepartmentTags")]
    public class DepartmentTag
    {
        [Required]
        public int TagID { get; set; }

        [Required]
        [MaxLength(50)]
        public string TagName { get; set; }

        [MaxLength(20)]
        public string? ShortName { get; set; }

        [Required]
        public int TagTypeID { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(7)]
        public string ColorHex { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public TagType TagType { get; set; }
        public ICollection<UserDepartment> UserDepartments { get; set; }
        public ICollection<AnnouncementTag> AnnouncementTags { get; set; }



    }
}
