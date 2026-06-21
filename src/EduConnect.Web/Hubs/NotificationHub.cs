using Microsoft.AspNetCore.SignalR;

namespace EduConnect.Web.Hubs
{
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var session = Context.GetHttpContext()?.Session;
            if (session != null)
            {
                await session.LoadAsync(Context.ConnectionAborted);
                var userIdStr = session.GetString("UserID");
                if (!string.IsNullOrEmpty(userIdStr))
                    await Groups.AddToGroupAsync(
                        Context.ConnectionId, $"user-{userIdStr}");
            }
            await base.OnConnectedAsync();
        }
    }
}
