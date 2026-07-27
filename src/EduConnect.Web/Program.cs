using System.Threading.RateLimiting;
using EduConnect.Web.Data;
using EduConnect.Web.Hubs;
using Google.GenAI;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("EduConnectDatabase")));

builder.Services.AddTransient<EduConnect.Web.Services.IEmailService, EduConnect.Web.Services.EmailService>();
builder.Services.AddScoped<EduConnect.Web.Services.IFeedRankingService, EduConnect.Web.Services.FeedRankingService>();
builder.Services.AddScoped<EduConnect.Web.Services.INotificationService, EduConnect.Web.Services.NotificationService>();
builder.Services.AddScoped<EduConnect.Web.Services.IBlobStorageService, EduConnect.Web.Services.BlobStorageService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(provider =>
    new Client(apiKey: provider.GetRequiredService<IConfiguration>()["GeminiSettings:ApiKey"] ?? ""));
builder.Services.AddScoped<EduConnect.Web.Services.IChatbotService, EduConnect.Web.Services.ChatbotService>();
builder.Services.AddHostedService<EduConnect.Web.Services.ChatExpiryService>();
builder.Services.AddSignalR();

// ↓ ADD THESE TWO
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax; 
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login / register / forgot-password: 5 attempts per minute per IP
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // AI chatbot: 15 messages per 5 minutes per logged-in user (IP if no session)
    options.AddPolicy("chatbot", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Session.GetString("UserID")
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();

        if (context.HttpContext.Request.Path.StartsWithSegments("/Chatbot"))
        {
            response.ContentType = "application/json";
            await response.WriteAsync(
                "{\"response\":\"You are sending messages too quickly. Please wait a few minutes and try again.\"}",
                cancellationToken);
        }
        else
        {
            response.ContentType = "text/html; charset=utf-8";
            await response.WriteAsync(
                "<!doctype html><html><head><title>Too Many Attempts</title></head>" +
                "<body style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;background:#f8f9fa;" +
                "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;\">" +
                "<div style=\"text-align:center;padding:2rem;\">" +
                "<h1 style=\"color:#002F6C;\">Too many attempts</h1>" +
                "<p style=\"color:#555;\">For security, further attempts are temporarily blocked.<br/>" +
                "Please wait a minute and try again.</p>" +
                "<a href=\"/Account/Login\" style=\"color:#1B4A8F;\">Back to login</a>" +
                "</div></body></html>",
                cancellationToken);
        }
    };
});
builder.Services.AddHttpContextAccessor();
// ↑ ADD THESE TWO

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add before builder.Build()
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Basic security headers on every response
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// After UseSession so the chatbot policy can partition by logged-in user
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<GroupChatHub>("/groupChatHub");
app.MapHub<EventHub>("/eventHub");

app.Run();
