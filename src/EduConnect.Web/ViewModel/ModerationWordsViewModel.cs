using EduConnect.Web.Models;

namespace EduConnect.Web.ViewModels
{
    // /Admin/ModerationWords: the words Group Finder stars out, and the
    // innocent words it never touches, each sorted A–Z.
    public class ModerationWordsViewModel
    {
        public List<ModerationWord> Prohibited { get; set; } = new();
        public List<ModerationWord> Allowed { get; set; } = new();
    }
}
