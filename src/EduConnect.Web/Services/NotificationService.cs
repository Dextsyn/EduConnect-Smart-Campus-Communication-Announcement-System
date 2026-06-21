using EduConnect.Web.Data;
using EduConnect.Web.Hubs;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.SignalR;

namespace EduConnect.Web.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hub;

        public NotificationService(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hub)
        {
            _context = context;
            _hub = hub;
        }

        public async Task SendAsync(int userId, string type, string message,
            string? link = null, int? announcementId = null)
        {
            var notification = new Notification
            {
                UserID = userId,
                Type = type,
                Message = message,
                Link = link,
                AnnouncementID = announcementId,
                SentAt = DateTime.Now
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            await _hub.Clients.Group($"user-{userId}")
                .SendAsync("ReceiveNotification", new
                {
                    notificationId = notification.NotificationID,
                    type = notification.Type,
                    message = notification.Message,
                    link = notification.Link,
                    sentAt = notification.SentAt
                });
        }

        public async Task SendToManyAsync(IEnumerable<int> userIds, string type,
            string message, string? link = null, int? announcementId = null)
        {
            var ids = userIds.ToList();
            if (ids.Count == 0) return;

            var now = DateTime.Now;
            var notifications = ids.Select(uid => new Notification
            {
                UserID = uid,
                Type = type,
                Message = message,
                Link = link,
                AnnouncementID = announcementId,
                SentAt = now
            }).ToList();

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();

            var dto = new
            {
                type,
                message,
                link,
                sentAt = now
            };

            await Task.WhenAll(notifications.Select(n =>
                _hub.Clients.Group($"user-{n.UserID}")
                    .SendAsync("ReceiveNotification", new
                    {
                        notificationId = n.NotificationID,
                        type = n.Type,
                        message = n.Message,
                        link = n.Link,
                        sentAt = n.SentAt
                    })));
        }
    }
}
