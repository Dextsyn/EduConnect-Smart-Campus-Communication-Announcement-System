using Microsoft.AspNetCore.SignalR;

namespace EduConnect.Web.Hubs
{
    public class EventHub : Hub
    {
        public async Task JoinEvent(int eventId)
        {
            var session = Context.GetHttpContext()?.Session;
            if (session == null) return;

            await session.LoadAsync(Context.ConnectionAborted);
            if (string.IsNullOrEmpty(session.GetString("UserID"))) return;

            await Groups.AddToGroupAsync(
                Context.ConnectionId, $"event-{eventId}");
        }
    }
}
