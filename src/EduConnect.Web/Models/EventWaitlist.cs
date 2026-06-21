using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("EventWaitlist")]
    public class EventWaitlist
    {
        [Key]
        public int WaitlistID { get; set; }

        [Required]
        public int EventID { get; set; }

        [Required]
        public int UserID { get; set; }

        public int Position { get; set; }

        [MaxLength(20)]
        public string Status { get; set; }
            = "Waiting";
        // Values: Waiting, Notified, Expired

        public DateTime JoinedAt { get; set; }
            = DateTime.Now;

        // Navigation
        public Event Event { get; set; }
        public User User { get; set; }
    }
}