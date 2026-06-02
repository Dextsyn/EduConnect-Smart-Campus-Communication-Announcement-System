# Group Finder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a student-only group finder with category filters, join/leave/dissolve lifecycle, and a SignalR-powered in-group chat that auto-expires 7 days after a group fills up.

**Architecture:** New `Group`, `GroupMember`, `GroupMessage` models in a single EF Core migration. A `GroupController` handles all CRUD/membership actions using session-based auth (matching the existing pattern). A `GroupChatHub` (SignalR) broadcasts messages in real time; a `ChatExpiryService` (BackgroundService) runs hourly to dissolve expired chats.

**Tech Stack:** ASP.NET Core 8 MVC · Entity Framework Core / SQL Server · SignalR (already wired in Program.cs) · Bootstrap 5 + Bootstrap Icons · Razor views

---

## File Map

**Create:**
- `EduConnect.Web/Models/Group.cs`
- `EduConnect.Web/Models/GroupMember.cs`
- `EduConnect.Web/Models/GroupMessage.cs`
- `EduConnect.Web/Controllers/GroupController.cs`
- `EduConnect.Web/Hubs/GroupChatHub.cs`
- `EduConnect.Web/Services/ChatExpiryService.cs`
- `EduConnect.Web/Views/Group/Index.cshtml`
- `EduConnect.Web/Views/Group/Create.cshtml`
- `EduConnect.Web/Views/Group/Details.cshtml`

**Modify:**
- `EduConnect.Web/Data/ApplicationDbContext.cs` — add DbSets + EF config
- `EduConnect.Web/Program.cs` — register hub route + hosted service
- `EduConnect.Web/Views/Shared/_Layout.cshtml` — add Group Finder nav link to every role block

---

### Task 1: Create the three model files

**Files:**
- Create: `EduConnect.Web/Models/Group.cs`
- Create: `EduConnect.Web/Models/GroupMember.cs`
- Create: `EduConnect.Web/Models/GroupMessage.cs`

- [ ] **Step 1: Create `EduConnect.Web/Models/Group.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Groups")]
    public class Group
    {
        [Key]
        public int GroupID { get; set; }

        [Required]
        public int CreatorID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; }

        [Required]
        public int MaxMembers { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Open";
        // Values: Open | Full | Dissolved

        public DateTime? ChatExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User Creator { get; set; }
        public ICollection<GroupMember> Members { get; set; }
        public ICollection<GroupMessage> Messages { get; set; }
    }
}
```

- [ ] **Step 2: Create `EduConnect.Web/Models/GroupMember.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("GroupMembers")]
    public class GroupMember
    {
        [Key]
        public int MembershipID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int UserID { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public Group Group { get; set; }
        public User User { get; set; }
    }
}
```

- [ ] **Step 3: Create `EduConnect.Web/Models/GroupMessage.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("GroupMessages")]
    public class GroupMessage
    {
        [Key]
        public int MessageID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int SenderID { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Content { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public Group Group { get; set; }
        public User Sender { get; set; }
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Models/Group.cs EduConnect.Web/Models/GroupMember.cs EduConnect.Web/Models/GroupMessage.cs
git commit -m "feat: add Group, GroupMember, GroupMessage models"
```

---

### Task 2: Register models in ApplicationDbContext

**Files:**
- Modify: `EduConnect.Web/Data/ApplicationDbContext.cs`

- [ ] **Step 1: Add three DbSets after the StudyGroupMembers set (around line 53)**

After this line:
```csharp
public DbSet<StudyGroupMember> StudyGroupMembers { get; set; }
```

Add:
```csharp
// Groups (Group Finder)
public DbSet<Group> Groups { get; set; }
public DbSet<GroupMember> GroupMembers { get; set; }
public DbSet<GroupMessage> GroupMessages { get; set; }
```

- [ ] **Step 2: Add EF Core configuration at the end of `OnModelCreating`, before the closing `}`**

After the `EventWaitlist` configuration block (around line 396), add:

```csharp
// ─── Groups ────────────────────────────
modelBuilder.Entity<Group>(entity =>
{
    entity.HasKey(e => e.GroupID);
    entity.HasOne(e => e.Creator)
          .WithMany()
          .HasForeignKey(e => e.CreatorID)
          .OnDelete(DeleteBehavior.Restrict);
});

// ─── GroupMembers ──────────────────────
modelBuilder.Entity<GroupMember>(entity =>
{
    entity.HasKey(e => e.MembershipID);
    entity.HasIndex(e => new { e.GroupID, e.UserID }).IsUnique();
    entity.HasOne(e => e.Group)
          .WithMany(e => e.Members)
          .HasForeignKey(e => e.GroupID)
          .OnDelete(DeleteBehavior.Restrict);
    entity.HasOne(e => e.User)
          .WithMany()
          .HasForeignKey(e => e.UserID)
          .OnDelete(DeleteBehavior.Restrict);
});

// ─── GroupMessages ─────────────────────
modelBuilder.Entity<GroupMessage>(entity =>
{
    entity.HasKey(e => e.MessageID);
    entity.HasOne(e => e.Group)
          .WithMany(e => e.Messages)
          .HasForeignKey(e => e.GroupID)
          .OnDelete(DeleteBehavior.Restrict);
    entity.HasOne(e => e.Sender)
          .WithMany()
          .HasForeignKey(e => e.SenderID)
          .OnDelete(DeleteBehavior.Restrict);
});
```

- [ ] **Step 3: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Data/ApplicationDbContext.cs
git commit -m "feat: register Group, GroupMember, GroupMessage in DbContext"
```

---

### Task 3: Create and apply the migration

**Files:**
- Auto-generated in `EduConnect.Web/Migrations/`

- [ ] **Step 1: Create the migration**

Run: `dotnet ef migrations add AddGroupFinder --project EduConnect.Web`
Expected: New files appear in `EduConnect.Web/Migrations/` prefixed with a timestamp and `AddGroupFinder`.

- [ ] **Step 2: Apply the migration**

Run: `dotnet ef database update --project EduConnect.Web`
Expected: `Done.`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Migrations/
git commit -m "feat: add AddGroupFinder migration"
```

---

### Task 4: Create GroupController

**Files:**
- Create: `EduConnect.Web/Controllers/GroupController.cs`

- [ ] **Step 1: Create the controller with all six actions**

```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class GroupController : Controller
    {
        private readonly ApplicationDbContext _context;

        public GroupController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private bool IsStudent() =>
            HttpContext.Session.GetString("RoleName") == "Student";

        // GET /Group
        public async Task<IActionResult> Index()
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var groups = await _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members)
                .Where(g => g.Status != "Dissolved")
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();

            return View(groups);
        }

        // GET /Group/Create
        public IActionResult Create()
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();
            return View();
        }

        // POST /Group/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string name, string category, string? description, int maxMembers)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category)
                || maxMembers < 2 || maxMembers > 50)
            {
                TempData["Error"] = "Please fill in all required fields correctly.";
                return View();
            }

            var group = new Group
            {
                CreatorID = GetUserID(),
                Name = name.Trim(),
                Category = category,
                Description = description?.Trim(),
                MaxMembers = maxMembers
            };
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            // Creator counts as first member
            _context.GroupMembers.Add(new GroupMember
            {
                GroupID = group.GroupID,
                UserID = GetUserID()
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = group.GroupID });
        }

        // GET /Group/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members).ThenInclude(m => m.User)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();

            var userId = GetUserID();
            var isMember = group.Members.Any(m => m.UserID == userId);

            ViewBag.IsMember = isMember;
            ViewBag.IsCreator = group.CreatorID == userId;
            ViewBag.UserID = userId;

            // Load last 50 messages in chronological order for members only
            ViewBag.Messages = isMember && group.Status != "Dissolved"
                ? (await _context.GroupMessages
                    .Where(m => m.GroupID == id)
                    .Include(m => m.Sender)
                    .OrderByDescending(m => m.SentAt)
                    .Take(50)
                    .ToListAsync())
                    .AsEnumerable()
                    .Reverse()
                    .ToList()
                : null;

            return View(group);
        }

        // POST /Group/Join/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();

            var group = await _context.Groups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();

            if (group.Status != "Open")
            {
                TempData["Error"] = "This group is not open for new members.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var userId = GetUserID();
            if (group.Members.Any(m => m.UserID == userId))
            {
                TempData["Error"] = "You are already in this group.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _context.GroupMembers.Add(new GroupMember { GroupID = id, UserID = userId });
            await _context.SaveChangesAsync();

            var memberCount = await _context.GroupMembers.CountAsync(m => m.GroupID == id);
            if (memberCount >= group.MaxMembers)
            {
                group.Status = "Full";
                group.ChatExpiresAt = DateTime.UtcNow.AddDays(7);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST /Group/Leave/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups.FindAsync(id);
            if (group == null) return NotFound();

            var userId = GetUserID();
            if (group.CreatorID == userId)
            {
                TempData["Error"] = "Creators cannot leave. Dissolve the group instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var membership = await _context.GroupMembers
                .FirstOrDefaultAsync(m => m.GroupID == id && m.UserID == userId);

            if (membership != null)
            {
                _context.GroupMembers.Remove(membership);
                if (group.Status == "Full")
                {
                    group.Status = "Open";
                    group.ChatExpiresAt = null;
                }
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /Group/Dissolve/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dissolve(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups
                .Include(g => g.Messages)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();
            if (group.CreatorID != GetUserID()) return Forbid();

            _context.GroupMessages.RemoveRange(group.Messages);
            group.Status = "Dissolved";
            group.ChatExpiresAt = null;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Controllers/GroupController.cs
git commit -m "feat: add GroupController with browse/create/join/leave/dissolve"
```

---

### Task 5: Create GroupChatHub and register it

**Files:**
- Create: `EduConnect.Web/Hubs/GroupChatHub.cs`
- Modify: `EduConnect.Web/Program.cs`

- [ ] **Step 1: Create `EduConnect.Web/Hubs/GroupChatHub.cs`**

```csharp
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
```

- [ ] **Step 2: Register the hub route in `EduConnect.Web/Program.cs`**

After the existing line:
```csharp
app.MapHub<NotificationHub>("/notificationHub");
```

Add:
```csharp
app.MapHub<GroupChatHub>("/groupChatHub");
```

- [ ] **Step 3: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Hubs/GroupChatHub.cs EduConnect.Web/Program.cs
git commit -m "feat: add GroupChatHub and register /groupChatHub SignalR route"
```

---

### Task 6: Create ChatExpiryService and register it

**Files:**
- Create: `EduConnect.Web/Services/ChatExpiryService.cs`
- Modify: `EduConnect.Web/Program.cs`

- [ ] **Step 1: Create `EduConnect.Web/Services/ChatExpiryService.cs`**

```csharp
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
```

- [ ] **Step 2: Register in `EduConnect.Web/Program.cs`**

After the existing line:
```csharp
builder.Services.AddScoped<EduConnect.Web.Services.IChatbotService, EduConnect.Web.Services.ChatbotService>();
```

Add:
```csharp
builder.Services.AddHostedService<EduConnect.Web.Services.ChatExpiryService>();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Services/ChatExpiryService.cs EduConnect.Web/Program.cs
git commit -m "feat: add ChatExpiryService to dissolve full groups after 7-day chat window"
```

---

### Task 7: Create Views/Group/Index.cshtml

**Files:**
- Create: `EduConnect.Web/Views/Group/Index.cshtml`

Create the `EduConnect.Web/Views/Group/` directory if it does not exist.

- [ ] **Step 1: Create the view**

```html
@model IEnumerable<EduConnect.Web.Models.Group>
@{
    ViewData["Title"] = "Group Finder";
    var role = Context.Session.GetString("RoleName");
}

<div class="d-flex justify-content-between align-items-center mb-3">
    <div>
        <h3 class="fw-bold mb-0">
            <i class="bi bi-people me-2 text-primary"></i>Group Finder
        </h3>
        <p class="text-muted mb-0 small">Find your people — academic or otherwise.</p>
    </div>
    @if (role == "Student")
    {
        <a href="/Group/Create" class="btn btn-primary">
            <i class="bi bi-plus-lg me-1"></i>Create Group
        </a>
    }
</div>

@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        @TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<!-- Category Filter Pills -->
<div class="mb-3 d-flex flex-wrap gap-2 align-items-center">
    <button class="btn btn-primary btn-sm category-filter active" data-filter="all">All</button>
    <span class="text-muted small">Academic:</span>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Study Group">Study Group</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Project Team">Project Team</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Research">Research</button>
    <span class="text-muted small ms-2">Interest:</span>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Hobby & Arts">Hobby &amp; Arts</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Sports & Fitness">Sports &amp; Fitness</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Tech & Gaming">Tech &amp; Gaming</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Volunteer & Advocacy">Volunteer &amp; Advocacy</button>
    <button class="btn btn-outline-secondary btn-sm category-filter" data-filter="Culture & Faith">Culture &amp; Faith</button>
</div>

<!-- Search -->
<div class="mb-4">
    <input type="text" id="group-search" class="form-control"
           placeholder="Search groups by name..." />
</div>

<!-- Cards -->
<div class="row g-3" id="group-cards">
    @if (!Model.Any())
    {
        <div class="col-12 text-center text-muted py-5">
            <i class="bi bi-people fs-1 d-block mb-2"></i>
            No groups yet. Be the first to create one!
        </div>
    }
    @foreach (var group in Model)
    {
        <div class="col-md-4 group-card"
             data-category="@group.Category"
             data-name="@group.Name.ToLower()">
            <div class="card h-100 shadow-sm">
                <div class="card-body">
                    <div class="d-flex justify-content-between align-items-start mb-2">
                        <span class="badge bg-primary">@group.Category</span>
                        @if (group.Status == "Full")
                        {
                            <span class="badge bg-danger">Full</span>
                        }
                        else
                        {
                            <span class="badge bg-success">Open</span>
                        }
                    </div>
                    <h6 class="fw-bold mb-1">@group.Name</h6>
                    <p class="text-muted small mb-2" style="min-height:2.5em">
                        @(group.Description?.Length > 100
                            ? group.Description.Substring(0, 100) + "…"
                            : (group.Description ?? "No description."))
                    </p>
                    <div class="d-flex justify-content-between align-items-center small text-muted">
                        <span><i class="bi bi-people me-1"></i>@group.Members.Count / @group.MaxMembers</span>
                        <span>by @group.Creator.FirstName @group.Creator.LastName</span>
                    </div>
                </div>
                <div class="card-footer bg-transparent border-0 pt-0">
                    <a href="/Group/Details/@group.GroupID"
                       class="btn btn-outline-primary btn-sm w-100">View Group</a>
                </div>
            </div>
        </div>
    }
</div>

<div id="no-results" class="text-center text-muted py-5 d-none">
    <i class="bi bi-search fs-1 d-block mb-2"></i>
    No groups match your search.
</div>

@section Scripts {
<script>
    document.querySelectorAll('.category-filter').forEach(btn => {
        btn.addEventListener('click', function () {
            document.querySelectorAll('.category-filter').forEach(b => {
                b.classList.remove('active', 'btn-primary');
                b.classList.add('btn-outline-secondary');
            });
            this.classList.add('active', 'btn-primary');
            this.classList.remove('btn-outline-secondary');
            filterGroups();
        });
    });

    document.getElementById('group-search').addEventListener('input', filterGroups);

    function filterGroups() {
        const filter = document.querySelector('.category-filter.active').dataset.filter;
        const search = document.getElementById('group-search').value.toLowerCase();
        let visible = 0;
        document.querySelectorAll('.group-card').forEach(card => {
            const matchCat = filter === 'all' || card.dataset.category === filter;
            const matchSearch = card.dataset.name.includes(search);
            card.style.display = matchCat && matchSearch ? '' : 'none';
            if (matchCat && matchSearch) visible++;
        });
        document.getElementById('no-results').classList.toggle('d-none', visible > 0);
    }
</script>
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Views/Group/Index.cshtml
git commit -m "feat: add Group Finder browse page (Index view)"
```

---

### Task 8: Create Views/Group/Create.cshtml

**Files:**
- Create: `EduConnect.Web/Views/Group/Create.cshtml`

- [ ] **Step 1: Create the view**

```html
@{
    ViewData["Title"] = "Create Group";
}

<div class="row justify-content-center">
    <div class="col-md-6">
        <div class="card shadow-sm">
            <div class="card-header bg-primary text-white">
                <h5 class="mb-0">
                    <i class="bi bi-plus-circle me-2"></i>Create a Group
                </h5>
            </div>
            <div class="card-body">
                @if (TempData["Error"] != null)
                {
                    <div class="alert alert-danger">@TempData["Error"]</div>
                }
                <form method="post" action="/Group/Create">
                    @Html.AntiForgeryToken()

                    <div class="mb-3">
                        <label class="form-label fw-semibold">
                            Group Name <span class="text-danger">*</span>
                        </label>
                        <input type="text" name="name" class="form-control"
                               maxlength="150" required
                               placeholder="e.g. DS101 Finals Study Group" />
                    </div>

                    <div class="mb-3">
                        <label class="form-label fw-semibold">
                            Category <span class="text-danger">*</span>
                        </label>
                        <select name="category" class="form-select" required>
                            <option value="" disabled selected>Select a category...</option>
                            <optgroup label="Academic">
                                <option value="Study Group">Study Group</option>
                                <option value="Project Team">Project Team</option>
                                <option value="Research">Research</option>
                            </optgroup>
                            <optgroup label="Interest">
                                <option value="Hobby & Arts">Hobby &amp; Arts</option>
                                <option value="Sports & Fitness">Sports &amp; Fitness</option>
                                <option value="Tech & Gaming">Tech &amp; Gaming</option>
                                <option value="Volunteer & Advocacy">Volunteer &amp; Advocacy</option>
                                <option value="Culture & Faith">Culture &amp; Faith</option>
                            </optgroup>
                        </select>
                    </div>

                    <div class="mb-3">
                        <label class="form-label fw-semibold">Description</label>
                        <textarea name="description" class="form-control" rows="3"
                                  maxlength="1000"
                                  placeholder="What is this group about?"></textarea>
                    </div>

                    <div class="mb-4">
                        <label class="form-label fw-semibold">
                            Max Members <span class="text-danger">*</span>
                        </label>
                        <input type="number" name="maxMembers" class="form-control"
                               min="2" max="50" value="10" required />
                        <div class="form-text">Between 2 and 50. You count as 1 toward this limit.</div>
                    </div>

                    <div class="d-flex gap-2">
                        <button type="submit" class="btn btn-primary">
                            <i class="bi bi-check-lg me-1"></i>Create Group
                        </button>
                        <a href="/Group" class="btn btn-outline-secondary">Cancel</a>
                    </div>
                </form>
            </div>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Views/Group/Create.cshtml
git commit -m "feat: add Group create form view"
```

---

### Task 9: Create Views/Group/Details.cshtml

**Files:**
- Create: `EduConnect.Web/Views/Group/Details.cshtml`

- [ ] **Step 1: Create the view**

```html
@model EduConnect.Web.Models.Group
@{
    ViewData["Title"] = Model.Name;
    bool isMember = (bool)ViewBag.IsMember;
    bool isCreator = (bool)ViewBag.IsCreator;
    int currentUserId = (int)ViewBag.UserID;
    var role = Context.Session.GetString("RoleName");
    var messages = ViewBag.Messages as List<EduConnect.Web.Models.GroupMessage>;
}

<div class="mb-3">
    <a href="/Group" class="btn btn-sm btn-outline-secondary">
        <i class="bi bi-arrow-left me-1"></i>Back to Groups
    </a>
</div>

@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        @TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="row g-3">
    <!-- Left: Group info + members -->
    <div class="col-md-4">
        <div class="card shadow-sm mb-3">
            <div class="card-body">
                <div class="d-flex gap-2 flex-wrap mb-2">
                    <span class="badge bg-primary">@Model.Category</span>
                    @if (Model.Status == "Full")
                    {
                        <span class="badge bg-danger">Full</span>
                    }
                    else if (Model.Status == "Dissolved")
                    {
                        <span class="badge bg-secondary">Dissolved</span>
                    }
                    else
                    {
                        <span class="badge bg-success">Open</span>
                    }
                </div>
                <h5 class="fw-bold mb-1">@Model.Name</h5>
                <p class="text-muted small">@(Model.Description ?? "No description provided.")</p>
                <hr class="my-2" />
                <div class="small mb-1">
                    <i class="bi bi-person me-1"></i>
                    Created by <strong>@Model.Creator.FirstName @Model.Creator.LastName</strong>
                </div>
                <div class="small mb-3">
                    <i class="bi bi-people me-1"></i>
                    <strong>@Model.Members.Count / @Model.MaxMembers</strong> members
                </div>

                @if (role == "Student" && !isCreator && Model.Status != "Dissolved")
                {
                    if (isMember)
                    {
                        <form method="post" action="/Group/Leave/@Model.GroupID">
                            @Html.AntiForgeryToken()
                            <button type="submit" class="btn btn-outline-danger btn-sm w-100"
                                    onclick="return confirm('Leave this group?')">
                                <i class="bi bi-box-arrow-left me-1"></i>Leave Group
                            </button>
                        </form>
                    }
                    else if (Model.Status == "Open")
                    {
                        <form method="post" action="/Group/Join/@Model.GroupID">
                            @Html.AntiForgeryToken()
                            <button type="submit" class="btn btn-primary btn-sm w-100">
                                <i class="bi bi-person-plus me-1"></i>Join Group
                            </button>
                        </form>
                    }
                    else
                    {
                        <button class="btn btn-secondary btn-sm w-100" disabled>Group is Full</button>
                    }
                }

                @if (isCreator && Model.Status != "Dissolved")
                {
                    <form method="post" action="/Group/Dissolve/@Model.GroupID" class="mt-2">
                        @Html.AntiForgeryToken()
                        <button type="submit" class="btn btn-outline-danger btn-sm w-100"
                                onclick="return confirm('Dissolve this group? All chat messages will be permanently deleted.')">
                            <i class="bi bi-trash me-1"></i>Dissolve Group
                        </button>
                    </form>
                }
            </div>
        </div>

        <!-- Members list -->
        <div class="card shadow-sm">
            <div class="card-header small fw-semibold text-uppercase py-2">
                <i class="bi bi-people me-1"></i>Members
            </div>
            <ul class="list-group list-group-flush">
                @foreach (var member in Model.Members)
                {
                    <li class="list-group-item d-flex align-items-center gap-2 py-2">
                        <img src="https://ui-avatars.com/api/?name=@(member.User.FirstName)+@(member.User.LastName)&background=0d6efd&color=fff&size=32"
                             class="rounded-circle" width="32" height="32" alt="" />
                        <div>
                            <div class="small fw-semibold">
                                @member.User.FirstName @member.User.LastName
                            </div>
                            @if (member.UserID == Model.CreatorID)
                            {
                                <span class="badge bg-primary" style="font-size:10px">Creator</span>
                            }
                        </div>
                    </li>
                }
            </ul>
        </div>
    </div>

    <!-- Right: Chat -->
    <div class="col-md-8">
        <div class="card shadow-sm" style="min-height:500px">
            <div class="card-header d-flex justify-content-between align-items-center">
                <span class="fw-semibold">
                    <i class="bi bi-chat-dots me-2"></i>Group Chat
                </span>
                @if (Model.ChatExpiresAt.HasValue)
                {
                    <small class="text-warning fw-semibold" id="expiry-countdown"
                           data-expires="@Model.ChatExpiresAt.Value.ToString("o")">
                        Calculating…
                    </small>
                }
            </div>
            <div class="card-body d-flex flex-column" style="height:480px">
                @if (Model.Status == "Dissolved")
                {
                    <div class="text-center text-muted m-auto">
                        <i class="bi bi-chat-slash fs-1 d-block mb-2"></i>
                        This group's chat has expired.
                    </div>
                }
                else if (!isMember)
                {
                    <div class="text-center text-muted m-auto">
                        <i class="bi bi-lock fs-1 d-block mb-2"></i>
                        @if (Model.Status == "Full")
                        {
                            <span>This group is full.</span>
                        }
                        else
                        {
                            <span>Join this group to access the chat.</span>
                        }
                    </div>
                }
                else
                {
                    <!-- Message feed -->
                    <div id="chat-messages" class="flex-grow-1 overflow-auto mb-3 d-flex flex-column">
                        @if (messages != null)
                        {
                            foreach (var msg in messages)
                            {
                                bool isOwn = msg.SenderID == currentUserId;
                                <div class="d-flex @(isOwn ? "justify-content-end" : "justify-content-start") mb-2">
                                    <div class="px-3 py-2 rounded-3 @(isOwn ? "bg-primary text-white" : "bg-light text-dark")"
                                         style="max-width:75%">
                                        @if (!isOwn)
                                        {
                                            <div class="fw-semibold small mb-1">
                                                @msg.Sender.FirstName @msg.Sender.LastName
                                            </div>
                                        }
                                        <div>@msg.Content</div>
                                        <div class="@(isOwn ? "text-white-50" : "text-muted") mt-1"
                                             style="font-size:11px">
                                            @msg.SentAt.ToLocalTime().ToString("hh:mm tt")
                                        </div>
                                    </div>
                                </div>
                            }
                        }
                    </div>

                    <!-- Input -->
                    <div class="d-flex gap-2 mt-auto">
                        <input type="text" id="chat-input" class="form-control"
                               placeholder="Type a message…" maxlength="2000"
                               onkeydown="if(event.key==='Enter'){event.preventDefault();sendMessage();}" />
                        <button class="btn btn-primary" onclick="sendMessage()">
                            <i class="bi bi-send"></i>
                        </button>
                    </div>
                }
            </div>
        </div>
    </div>
</div>

@section Scripts {
@if (isMember && Model.Status != "Dissolved")
{
<script>
    const groupId = @Model.GroupID;
    const currentUserId = @currentUserId;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(`/groupChatHub?groupId=${groupId}`)
        .build();

    connection.on("ReceiveMessage", function (data) {
        const isOwn = data.senderId === currentUserId;
        appendMessage(data.senderName, data.content, data.sentAt, isOwn);
    });

    connection.start().catch(err => console.error(err));

    function esc(str) {
        const d = document.createElement('div');
        d.textContent = str;
        return d.innerHTML;
    }

    function appendMessage(senderName, content, sentAt, isOwn) {
        const feed = document.getElementById('chat-messages');
        const el = document.createElement('div');
        el.className = `d-flex ${isOwn ? 'justify-content-end' : 'justify-content-start'} mb-2`;
        el.innerHTML = `
            <div class="px-3 py-2 rounded-3 ${isOwn ? 'bg-primary text-white' : 'bg-light text-dark'}"
                 style="max-width:75%">
                ${!isOwn ? `<div class="fw-semibold small mb-1">${esc(senderName)}</div>` : ''}
                <div>${esc(content)}</div>
                <div class="${isOwn ? 'text-white-50' : 'text-muted'} mt-1"
                     style="font-size:11px">${esc(sentAt)}</div>
            </div>`;
        feed.appendChild(el);
        feed.scrollTop = feed.scrollHeight;
    }

    function sendMessage() {
        const input = document.getElementById('chat-input');
        const content = input.value.trim();
        if (!content) return;
        connection.invoke("SendMessage", groupId, content).catch(err => console.error(err));
        input.value = '';
    }

    // Scroll to bottom on load
    const feed = document.getElementById('chat-messages');
    if (feed) feed.scrollTop = feed.scrollHeight;

    // Expiry countdown
    const expiryEl = document.getElementById('expiry-countdown');
    if (expiryEl) {
        const expires = new Date(expiryEl.dataset.expires);
        function updateCountdown() {
            const diff = expires - new Date();
            if (diff <= 0) { expiryEl.textContent = 'Chat expired'; return; }
            const d = Math.floor(diff / 86400000);
            const h = Math.floor((diff % 86400000) / 3600000);
            const m = Math.floor((diff % 3600000) / 60000);
            expiryEl.textContent = `Chat expires in ${d}d ${h}h ${m}m`;
        }
        updateCountdown();
        setInterval(updateCountdown, 60000);
    }
</script>
}
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Views/Group/Details.cshtml
git commit -m "feat: add Group detail view with real-time chat and expiry countdown"
```

---

### Task 10: Add Group Finder to sidebar navigation

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml`

Add a "Group Finder" link to each role block in the sidebar. All logged-in roles can browse; non-students simply won't see Create/Join buttons once they arrive.

- [ ] **Step 1: Add to Administrator block**

After the existing Events link in the Administrator block:
```html
<li class="nav-item">
    <a href="/Event"
       class="nav-link text-dark">
        <i class="bi bi-calendar-event me-2"></i>
        Events
    </a>
</li>
```

Add:
```html
<li class="nav-item">
    <a href="/Group"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Group Finder
    </a>
</li>
```

- [ ] **Step 2: Add to Dean block**

After the Events link in the Dean block, add the same snippet:
```html
<li class="nav-item">
    <a href="/Group"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Group Finder
    </a>
</li>
```

- [ ] **Step 3: Add to Faculty block**

After the Events link in the Faculty block, add:
```html
<li class="nav-item">
    <a href="/Group"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Group Finder
    </a>
</li>
```

- [ ] **Step 4: Add to Staff block**

After the Events link in the Staff block, add:
```html
<li class="nav-item">
    <a href="/Group"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Group Finder
    </a>
</li>
```

- [ ] **Step 5: Replace Study Groups link in Student (else) block**

Find and replace the existing Study Groups link:
```html
<li class="nav-item">
    <a href="/StudyGroup"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Study Groups
    </a>
</li>
```

Replace with:
```html
<li class="nav-item">
    <a href="/Group"
       class="nav-link text-dark">
        <i class="bi bi-people me-2"></i>
        Group Finder
    </a>
</li>
```

- [ ] **Step 6: Build and run manual verification**

Run: `dotnet build EduConnect.Web`
Expected: `Build succeeded.`

Run: `dotnet run --project EduConnect.Web`

Manually verify all of these in order:

1. **Browse page** — log in as a Student; sidebar shows "Group Finder"; clicking it loads `/Group` with category pills and search bar
2. **Create** — click "Create Group"; fill in name, category, description, max members (e.g. 2); submit; lands on the group's Detail page with you as the only member and creator badge
3. **Chat (as creator)** — type a message and press Enter or click Send; message appears in the feed
4. **Join + fill group** — log in as a second Student in a separate browser; navigate to the group; click "Join Group"; member count reads "2/2"; status badge shows "Full"; chat expiry countdown appears
5. **Real-time chat** — with both browsers open on the Details page, send a message from each account; messages appear instantly in both windows without page refresh
6. **Leave reopens slot** — second Student clicks "Leave Group"; redirects to browse; status reverts to "Open" on the group card
7. **Dissolve** — creator clicks "Dissolve Group"; confirms; redirects to browse; group no longer appears in the list
8. **Non-student view** — log in as Faculty; "Group Finder" is in sidebar; groups visible; no "Create Group" button; no "Join Group" button on detail page

- [ ] **Step 7: Commit**

```bash
git add EduConnect.Web/Views/Shared/_Layout.cshtml
git commit -m "feat: add Group Finder nav link to all role sidebars"
```
