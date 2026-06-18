using Microsoft.AspNetCore.SignalR;

namespace EduConnect.Web.Hubs
{
    public class EventHub : Hub
    {
        public async Task JoinEvent(int eventId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId, $"event-{eventId}");
        }
    }
}
