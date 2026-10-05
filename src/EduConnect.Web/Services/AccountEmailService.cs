using System.Security.Cryptography;
using System.Text;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;
using static System.Net.WebUtility;

namespace EduConnect.Web.Services
{
    // The account emails that carry a credential: the confirmation link a
    // self-registered student must click, and the temporary password an
    // admin-created user logs in with. Owns the tokens and passwords so the
    // controllers only decide when to send.
    public interface IAccountEmailService
    {
        // Replaces the user's unused links with a fresh one, saves, then
        // emails it. Throws if the email can't be sent.
        Task SendVerificationAsync(User user);

        // The user the token was issued to, with the token marked used, or
        // null if it is unknown, used or expired. The caller saves.
        Task<User?> ConfirmAsync(string token);

        // Whether the user holds a confirmation link that still works.
        Task<bool> HasLiveVerificationAsync(int userId);

        // Gives the user a new random password that must be changed on
        // first login and expires after TempPasswordLifetime. Returns it so
        // it can be emailed; it is never stored. The caller saves.
        string IssueTemporaryPassword(User user);

        // Emails a password from IssueTemporaryPassword. Throws if the
        // email can't be sent.
        Task SendTemporaryPasswordAsync(User user, string temporaryPassword);
    }

    public class AccountEmailService : IAccountEmailService
    {
        public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
        public static readonly TimeSpan TempPasswordLifetime = TimeSpan.FromHours(72);

        // No look-alikes (0/O, 1/l/I) so the password can be typed from the email.
        private const string PasswordAlphabet =
            "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        private const int PasswordLength = 12;

        private readonly ApplicationDbContext _context;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;

        public AccountEmailService(
            ApplicationDbContext context,
            IEmailService email,
            IConfiguration config,
            IHttpContextAccessor http)
        {
            _context = context;
            _email = email;
            _config = config;
            _http = http;
        }

        public async Task SendVerificationAsync(User user)
        {
            var stale = await _context.EmailVerificationTokens
                .Where(t => t.UserID == user.UserID && !t.IsUsed)
                .ToListAsync();
            _context.EmailVerificationTokens.RemoveRange(stale);

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            _context.EmailVerificationTokens.Add(new EmailVerificationToken
            {
                UserID = user.UserID,
                TokenHash = Hash(token),
                ExpiresAt = DateTime.Now.Add(VerificationLifetime),
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();

            var link = $"{BaseUrl()}/Account/ConfirmEmail?token={Uri.EscapeDataString(token)}";

            await _email.SendEmailAsync(
                user.Email,
                $"{user.FirstName} {user.LastName}",
                "EduConnect — Confirm your email",
                Layout("Confirm your email", $@"
                <p>Hi {HtmlEncode(user.FirstName)},</p>
                <p>Thanks for registering for EduConnect. Confirm that this is your
                   Adamson email so ITC can review your account.</p>
                <p>This link expires in <strong>24 hours</strong>.</p>
                {Button(link, "Confirm my email")}
                <p style='color:#999;font-size:13px;'>If you didn't register, ignore this
                   email and no account will be created.</p>
                {CopyLink(link)}"));
        }

        public async Task<User?> ConfirmAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var hash = Hash(token);
            var row = await _context.EmailVerificationTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t =>
                    t.TokenHash == hash && !t.IsUsed && t.ExpiresAt > DateTime.Now);

            if (row == null)
                return null;

            row.IsUsed = true;
            return row.User;
        }

        public Task<bool> HasLiveVerificationAsync(int userId) =>
            _context.EmailVerificationTokens.AnyAsync(t =>
                t.UserID == userId && !t.IsUsed && t.ExpiresAt > DateTime.Now);

        public string IssueTemporaryPassword(User user)
        {
            var password = RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.MustChangePassword = true;
            user.TempPasswordExpiresAt = DateTime.Now.Add(TempPasswordLifetime);
            user.UpdatedAt = DateTime.Now;
            return password;
        }

        public Task SendTemporaryPasswordAsync(User user, string temporaryPassword)
        {
            var login = $"{BaseUrl()}/Account/Login";

            return _email.SendEmailAsync(
                user.Email,
                $"{user.FirstName} {user.LastName}",
                "EduConnect — Your account details",
                Layout("Your EduConnect account", $@"
                <p>Hi {HtmlEncode(user.FirstName)},</p>
                <p>ITC has set up an EduConnect account for you. Log in with:</p>
                <table style='margin:20px 0;border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px 4px 0;color:#666;'>Email</td>
                        <td style='padding:4px 0;'><strong>{HtmlEncode(user.Email)}</strong></td></tr>
                    <tr><td style='padding:4px 12px 4px 0;color:#666;'>Temporary password</td>
                        <td style='padding:4px 0;font-family:Consolas,monospace;font-size:16px;'>
                            <strong>{HtmlEncode(temporaryPassword)}</strong></td></tr>
                </table>
                <p>You'll be asked to choose your own password when you log in.
                   The temporary password expires in <strong>72 hours</strong>; after
                   that, ask ITC to send a new one.</p>
                {Button(login, "Log in to EduConnect")}
                <p style='color:#999;font-size:13px;'>Don't share this email.
                   If you weren't expecting an account, contact ITC.</p>"));
        }

        private string BaseUrl()
        {
            var configured = _config["AppBaseUrl"]?.TrimEnd('/');
            if (!string.IsNullOrEmpty(configured))
                return configured;

            var request = _http.HttpContext?.Request;
            return request == null ? "" : $"{request.Scheme}://{request.Host}";
        }

        private static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string Button(string href, string text) => $@"
                <div style='text-align:center;margin:30px 0;'>
                    <a href='{HtmlEncode(href)}'
                       style='background:#0d6efd;color:white;padding:14px 30px;
                              text-decoration:none;border-radius:6px;font-weight:bold;
                              display:inline-block;'>{text}</a>
                </div>";

        private static string CopyLink(string href) => $@"
                <hr style='border:none;border-top:1px solid #ddd;margin:20px 0;' />
                <p style='color:#999;font-size:12px;'>Or copy this link to your browser:<br/>
                    <a href='{HtmlEncode(href)}' style='color:#0d6efd;word-break:break-all;'>{HtmlEncode(href)}</a>
                </p>";

        private static string Layout(string heading, string body) => $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;'>
            <div style='background:#0d6efd;padding:30px;text-align:center;border-radius:8px 8px 0 0;'>
                <h1 style='color:white;margin:0;'>EduConnect</h1>
                <p style='color:rgba(255,255,255,0.8);margin:5px 0 0;'>Adamson University</p>
            </div>
            <div style='background:#f8f9fa;padding:30px;border-radius:0 0 8px 8px;color:#333;'>
                <h2 style='color:#333;'>{heading}</h2>
                {body}
            </div>
        </div>";
    }
}
