using EduConnect.Web.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        public long LogID { get; set; }

        // The actor. Set to null when that user is deleted; ActorName
        // keeps who it was.
        public int? UserID { get; set; }

        [MaxLength(150)]
        public string? ActorName { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; }

        [Required]
        [MaxLength(100)]
        public string TableAffected { get; set; }

        public int? RecordID { get; set; }

        // One readable line for the Audit Log page.
        [MaxLength(500)]
        public string? Summary { get; set; }

        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        [MaxLength(45)]
        public string? IPAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public User? User { get; set; }
    }
}