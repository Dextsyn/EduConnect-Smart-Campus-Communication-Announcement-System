using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EduConnect.Web.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(
            string toEmail,
            string toName,
            string subject,
            string htmlBody);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;
        private readonly IWebHostEnvironment _env;

        public EmailService(
            IConfiguration config,
            ILogger<EmailService> logger,
            IWebHostEnvironment env)
        {
            _config = config;
            _logger = logger;
            _env = env;
        }

        public async Task SendEmailAsync(
            string toEmail,
            string toName,
            string subject,
            string htmlBody)
        {
            try
            {
                _logger.LogInformation(
                    "Sending email to {Email}",
                    toEmail);

                var email = new MimeMessage();

                email.From.Add(new MailboxAddress(
                    _config["EmailSettings:SenderName"],
                    _config["EmailSettings:SenderEmail"]
                ));

                email.To.Add(new MailboxAddress(
                    toName, toEmail));

                email.Subject = subject;

                email.Body = new BodyBuilder
                {
                    HtmlBody = htmlBody
                }.ToMessageBody();

                using var smtp = new SmtpClient();

                // ← KEY FIX
                // Skip SSL validation in development
                if (_env.IsDevelopment())
                {
                    smtp.ServerCertificateValidationCallback
                        = (s, c, h, e) => true;
                }

                await smtp.ConnectAsync(
                    _config["EmailSettings:SmtpHost"],
                    int.Parse(
                        _config["EmailSettings:SmtpPort"]),
                    SecureSocketOptions.StartTls);

                await smtp.AuthenticateAsync(
                    _config["EmailSettings:SenderEmail"],
                    _config["EmailSettings:Password"]);

                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);

                _logger.LogInformation(
                    "✅ Email sent to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "❌ Email failed: {Error}",
                    ex.Message);
                throw;
            }
        }
    }
}