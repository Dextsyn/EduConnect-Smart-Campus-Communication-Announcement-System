using Google.GenAI;
using Google.GenAI.Types;

namespace EduConnect.Web.Services
{
    // Gemini behind smart tagging, asked for a JSON reply. Any failure
    // (no key, quota, network, safety block) returns null so the caller
    // falls back to keyword rules.
    public class GeminiSmartTagModel : ISmartTagModel
    {
        private readonly Client _client;
        private readonly IConfiguration _config;
        private readonly ILogger<GeminiSmartTagModel> _logger;

        public GeminiSmartTagModel(Client client, IConfiguration config, ILogger<GeminiSmartTagModel> logger)
        {
            _client = client;
            _config = config;
            _logger = logger;
        }

        public string Name => _config["GeminiSettings:Model"] ?? "gemini-2.5-flash";

        public async Task<SmartTagModelReply?> AskAsync(string systemPrompt, string userText)
        {
            if (string.IsNullOrWhiteSpace(_config["GeminiSettings:ApiKey"]))
                return null;

            try
            {
                var response = await _client.Models.GenerateContentAsync(
                    model: Name,
                    contents: new List<Content>
                    {
                        new Content { Role = "user", Parts = new List<Part> { new Part { Text = userText } } }
                    },
                    config: new GenerateContentConfig
                    {
                        SystemInstruction = new Content
                        {
                            Parts = new List<Part> { new Part { Text = systemPrompt } }
                        },
                        ResponseMimeType = "application/json",
                        Temperature = 0.2,
                        // Caps what one click can cost. Thinking is off: it
                        // was most of the tokens and of the wait per click.
                        MaxOutputTokens = _config.GetValue("SmartTag:MaxOutputTokens", 1024),
                        ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
                    });

                return string.IsNullOrWhiteSpace(response.Text)
                    ? null
                    : new SmartTagModelReply(response.Text, response.UsageMetadata?.TotalTokenCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Smart tag Gemini call failed: {Message}", ex.Message);
                return null;
            }
        }
    }
}
