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

        public int? UserID { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; }

        [Required]
        [MaxLength(100)]
        public string TableAffected { get; set; }

        public int? RecordID { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        [MaxLength(45)]
        public string? IPAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public User? User { get; set; }
    }
}