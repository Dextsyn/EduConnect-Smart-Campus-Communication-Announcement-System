using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EduConnect.Web.Controllers
{
    public class ChatbotController : Controller
    {
        private readonly IChatbotService _chatbotService;

        public ChatbotController(IChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private string GetRoleName() =>
            HttpContext.Session.GetString("RoleName") ?? "";

        private string GetOrCreateSessionToken()
        {
            var token = HttpContext.Session.GetString("ChatSessionToken");
            if (string.IsNullOrEmpty(token))
            {
                token = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetString("ChatSessionToken", token);
            }
            return token;
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            if (!IsLoggedIn())
                return Json(new { error = "Unauthorized" });

            var token = HttpContext.Session.GetString("ChatSessionToken");
            if (string.IsNullOrEmpty(token))
                return Json(Array.Empty<object>());

            var history = await _chatbotService.GetHistoryAsync(token);

            var result = history.SelectMany(h => new object[]
            {
                new { role = "user", message = h.UserMessage, timestamp = h.CreatedAt.ToString("hh:mm tt") },
                new { role = "bot",  message = h.BotResponse, timestamp = h.CreatedAt.ToString("hh:mm tt") }
            }).ToList();

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] ChatSendRequest request)
        {
            if (!IsLoggedIn())
                return Json(new { error = "Unauthorized" });

            if (string.IsNullOrWhiteSpace(request?.Message))
                return Json(new { error = "Message cannot be empty" });

            if (request.Message.Length > 1000)
                return Json(new { error = "Message too long." });

            var token = GetOrCreateSessionToken();
            var response = await _chatbotService.SendMessageAsync(
                GetUserID(), GetRoleName(), token, request.Message.Trim());

            return Json(new { response });
        }

        public class ChatSendRequest
        {
            public string? Message { get; set; }
        }
    }
}
