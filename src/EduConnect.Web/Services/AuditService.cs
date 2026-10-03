using System.Text.Json;
using EduConnect.Web.Data;
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _http;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor http)
        {
            _context = context;
            _http = http;
        }

        public void Record(string action, string area, int? recordId, string summary,
            object? oldValues = null, object? newValues = null)
        {
            var http = _http.HttpContext;
            var session = http?.Session;
            int? actorId = int.TryParse(session?.GetString("UserID"), out var id) ? id : null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = actorId,
                ActorName = Truncate(session?.GetString("UserName"), 150),
                Action = action,
                TableAffected = area,
                RecordID = recordId,
                Summary = Truncate(summary, 500),
                OldValues = oldValues == null ? null : JsonSerializer.Serialize(oldValues),
                NewValues = newValues == null ? null : JsonSerializer.Serialize(newValues),
                IPAddress = Truncate(ClientIp(http), 45),
                CreatedAt = DateTime.Now
            });
        }

        // App Service sits behind a front end that puts the caller first in
        // X-Forwarded-For, sometimes with a port.
        private static string? ClientIp(HttpContext? http)
        {
            if (http == null) return null;

            var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                // "1.2.3.4:5678" → "1.2.3.4"; IPv6 ("[::1]:5678" or bare) is left alone.
                if (first.Count(c => c == ':') == 1)
                    first = first[..first.IndexOf(':')];
                return first;
            }

            return http.Connection.RemoteIpAddress?.ToString();
        }

        private static string? Truncate(string? value, int max) =>
            value == null || value.Length <= max ? value : value[..max];
    }
}
