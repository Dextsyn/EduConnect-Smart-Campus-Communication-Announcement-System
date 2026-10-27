namespace EduConnect.Web.Services
{
    // Group Finder word moderation over the ModerationWords list.
    public interface IContentModerationService
    {
        // True when the text holds a listed word (group name, description).
        Task<bool> ContainsProhibitedAsync(string? text);

        // The text with each listed word starred out (chat messages).
        Task<string> MaskAsync(string text);

        // Call after changing ModerationWords so the next check reloads it.
        void InvalidateCache();
    }
}
