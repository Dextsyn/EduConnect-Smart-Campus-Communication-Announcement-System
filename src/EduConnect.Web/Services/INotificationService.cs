namespace EduConnect.Web.Services
{
    public interface INotificationService
    {
        Task SendAsync(int userId, string type, string message,
            string? link = null, int? announcementId = null);

        Task SendToManyAsync(IEnumerable<int> userIds, string type,
            string message, string? link = null, int? announcementId = null);
    }
}
