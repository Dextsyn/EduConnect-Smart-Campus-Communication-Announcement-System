using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public interface IChatbotService
    {
        Task<string> SendMessageAsync(int userId, string roleName, string sessionToken, string userMessage);
        Task<List<ChatbotConversation>> GetHistoryAsync(string sessionToken);
    }
}
