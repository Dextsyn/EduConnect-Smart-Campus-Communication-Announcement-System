using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("ChatbotConversations")]
    public class ChatbotConversation
    {
        [Key]
        public int ConversationID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [MaxLength(255)]
        public string SessionToken { get; set; }

        [Required]
        public string UserMessage { get; set; }

        [Required]
        public string BotResponse { get; set; }

        [MaxLength(100)]
        public string? IntentDetected { get; set; }

        public bool? WasHelpful { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public User User { get; set; }
    }
}