using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("GroupMessages")]
    public class GroupMessage
    {
        [Key]
        public int MessageID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int SenderID { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Content { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public Group Group { get; set; }
        public User Sender { get; set; }
    }
}
