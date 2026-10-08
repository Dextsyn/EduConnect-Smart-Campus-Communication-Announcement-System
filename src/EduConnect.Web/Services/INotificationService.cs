namespace EduConnect.Web.Services
{
    public interface INotificationService
    {
        Task SendAsync(int userId, string type, string message,
            string? link = null, int? announcementId = null);

        Task SendToManyAsync(IEnumerable<int> userIds, string type,
            string message, string? link = null, int? announcementId = null);

        // A live update for open pages only: pushed over the hub, never
        // stored and never shown in the bell.
        Task SignalAsync(IEnumerable<int> userIds, string eventName, object payload);
    }
}
