using EduConnect.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class ChatExpiryService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ChatExpiryService> _logger;

        public ChatExpiryService(IServiceProvider services, ILogger<ChatExpiryService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExpireChats();
                await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
            }
        }

        private async Task ExpireChats()
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var expired = await db.Groups
                .Where(g => g.Status == "Full" && g.ChatExpiresAt <= DateTime.UtcNow)
                .Include(g => g.Messages)
                .ToListAsync();

            if (!expired.Any()) return;

            foreach (var group in expired)
            {
                db.GroupMessages.RemoveRange(group.Messages);
                group.Status = "Dissolved";
                group.ChatExpiresAt = null;
                _logger.LogInformation(
                    "Dissolved group {GroupID} ({Name}) — chat expired", group.GroupID, group.Name);
            }

            await db.SaveChangesAsync();
        }
    }
}
