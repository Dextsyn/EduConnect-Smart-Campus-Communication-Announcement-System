using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // One entry on the Group Finder moderation list: a word or phrase to
    // star out (IsAllowed = false), or an innocent word never to touch.
    // Stored normalised (ModerationMatcher.NormalizeEntry).
    [Table("ModerationWords")]
    public class ModerationWord
    {
        [Key]
        public int ModerationWordID { get; set; }

        [Required]
        [MaxLength(100)]
        public string Word { get; set; }

        // ModerationLanguages; null for words imported from the setting.
        [MaxLength(20)]
        public string? Language { get; set; }

        public bool IsAllowed { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class ModerationLanguages
    {
        public const string English = "English";
        public const string Tagalog = "Tagalog";
        public const string Taglish = "Taglish";

        public static readonly string[] All = { English, Tagalog, Taglish };
    }
}
