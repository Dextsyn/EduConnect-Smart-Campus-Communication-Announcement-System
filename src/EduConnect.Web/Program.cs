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
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<GroupChatHub>("/groupChatHub");
app.MapHub<EventHub>("/eventHub");

app.Run();
