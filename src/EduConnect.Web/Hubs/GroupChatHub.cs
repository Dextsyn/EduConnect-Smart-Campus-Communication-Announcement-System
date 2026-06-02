using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Hubs
{
    public class GroupChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public GroupChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            var ctx = Context.GetHttpContext();
            var session = ctx?.Session;
            var groupIdStr = ctx?.Request.Query["groupId"].ToString();

            if (session != null && int.TryParse(groupIdStr, out int groupId))
            {
                await session.LoadAsync(Context.ConnectionAborted);
                var userIdStr = session.GetString("UserID");
                if (int.TryParse(userIdStr, out int userId))
                {
                    var isMember = await _context.GroupMembers
                        .AnyAsync(m => m.GroupID == groupId && m.UserID == userId);
                    if (isMember)
                        await Groups.AddToGroupAsync(Context.ConnectionId, $"group-{groupId}");
                }
            }

            await base.OnConnectedAsync();
        }

        public async Task SendMessage(int groupId, string content)
        {
            var ctx = Context.GetHttpContext();
            var session = ctx?.Session;
            if (session == null) return;

            await session.LoadAsync(Context.ConnectionAborted);
            var userIdStr = session.GetString("UserID");
            if (!int.TryParse(userIdStr, out int userId)) return;

            if (string.IsNullOrWhiteSpace(content) || content.Length > 2000) return;

            var group = await _context.Groups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.GroupID == groupId);

            if (group == null || group.Status == "Dissolved") return;
            if (!group.Members.Any(m => m.UserID == userId)) return;

            var message = new GroupMessage
            {
                GroupID = groupId,
                SenderID = userId,
                Content = content.Trim()
            };
            _context.GroupMessages.Add(message);
            await _context.SaveChangesAsync();

            var sender = await _context.Users.FindAsync(userId);
            var senderName = $"{sender!.FirstName} {sender.LastName}";

            await Clients.Group($"group-{groupId}").SendAsync("ReceiveMessage", new
            {
                senderId = userId,
                senderName,
                content = message.Content,
                sentAt = message.SentAt.ToLocalTime().ToString("hh:mm tt")
            });
        }
    }
}
