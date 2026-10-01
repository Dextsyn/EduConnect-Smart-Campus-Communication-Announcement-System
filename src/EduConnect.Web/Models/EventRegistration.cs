using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("EventRegistrations")]
    public class EventRegistration
    {
        [Key]
        public int RegistrationID { get; set; }

        [Required]
        public int EventID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; }
            = "Registered";
        // Values: Registered, Cancelled, Attended

        [MaxLength(500)]
        public string? QRCode { get; set; }
        // Stores the relative URL path to the saved PNG file

        public DateTime RegisteredAt { get; set; }
            = DateTime.Now;

        // When the QR code was scanned (or attendance marked by hand).
        // NULL until the student attends.
        public DateTime? CheckedInAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Event Event { get; set; }
        public User User { get; set; }
    }
}