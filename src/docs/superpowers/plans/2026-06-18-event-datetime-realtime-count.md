# Event: Past-Date Graying & Real-Time Attendee Count Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gray out past dates on the event Create form and update the attendee slot counter on the Details page in real time when any user registers or cancels.

**Architecture:** Feature 1 is a pure JS change — `min`/`max` attributes on `datetime-local` inputs. Feature 2 adds a new `EventHub` (SignalR) that scopes broadcasts to `event-{id}` groups; after Register/CancelRegistration save, the controller broadcasts the new count; the Details page client joins the group and updates three DOM elements in-place.

**Tech Stack:** ASP.NET Core 8 MVC, SignalR (already wired via `NotificationHub`/`GroupChatHub`), Razor, vanilla JS.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Views/Event/Create.cshtml` | Modify | Add `min`/`max` constraints to all three datetime-local inputs |
| `Hubs/EventHub.cs` | Create | SignalR hub; exposes `JoinEvent(int)` so clients join `event-{id}` group |
| `Program.cs` | Modify | Map `/eventHub` route |
| `Controllers/EventController.cs` | Modify | Inject `IHubContext<EventHub>`, broadcast after Register and CancelRegistration |
| `Views/Event/Details.cshtml` | Modify | Add element IDs to slot counter, add `@section Scripts` with SignalR client |

---

## Task 1: Past-Date Graying on Create Form

**Files:**
- Modify: `EduConnect.Web/Views/Event/Create.cshtml` (lines 302–383, the `@section Scripts` block)

The existing `DOMContentLoaded` block already declares `formatDateTimeLocal`, `startField`, and `endField`, and has a `change` listener on `startField`. We add three things: a `regDeadline` reference, a `nowStr` minimum, and `min`/`max` wiring.

- [ ] **Step 1: Replace the Scripts section**

Find this block in `Create.cshtml` (starts around line 302):

```javascript
@section Scripts {
    <script>
        function previewPhoto(input) {
            const preview = document
                .getElementById('photoPreview');
            const img = document
                .getElementById('previewImg');
            if (input.files && input.files[0]) {
                const reader = new FileReader();
                reader.onload = e => {
                    img.src = e.target.result;
                    preview.classList.remove('d-none');
                };
                reader.readAsDataURL(input.files[0]);
            } else {
                preview.classList.add('d-none');
            }
        }

        function toggleOnline() {
            const isOnline = document
                .getElementById('isOnlineSwitch')
                .checked;
            const meetingDiv = document
                .getElementById('meetingUrlDiv');
            meetingDiv.style.display =
                isOnline ? 'block' : 'none';
        }
                // Set default datetime values
        // formatted correctly for datetime-local input
        document.addEventListener('DOMContentLoaded',
        function() {

            function formatDateTimeLocal(date) {
                // Format: YYYY-MM-DDTHH:MM
                // No seconds or milliseconds
                const year  = date.getFullYear();
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const day   = String(date.getDate()).padStart(2, '0');
                const hours = String(date.getHours()).padStart(2, '0');
                const mins  = String(date.getMinutes()).padStart(2, '0');

                return `${year}-${month}-${day}T${hours}:${mins}`;
            }

            // Set start to tomorrow at 9AM
            var tomorrow = new Date();
            tomorrow.setDate(tomorrow.getDate() + 1);
            tomorrow.setHours(9, 0, 0, 0);

            // Set end to tomorrow at 11AM
            var tomorrowEnd = new Date();
            tomorrowEnd.setDate(tomorrowEnd.getDate() + 1);
            tomorrowEnd.setHours(11, 0, 0, 0);

            var startField = document.getElementById('startDateTime');
            var endField   = document.getElementById('endDateTime');

            // Only set if field is empty
            if (startField && !startField.value)
                startField.value = formatDateTimeLocal(tomorrow);

            if (endField && !endField.value)
                endField.value = formatDateTimeLocal(tomorrowEnd);

            // Auto update end time when start changes
            if (startField)
            {
                startField.addEventListener('change',
                function() {
                    if (this.value && endField)
                    {
                        var start = new Date(this.value);
                        var end   = new Date(start);
                        end.setHours(end.getHours() + 2);
                        endField.value = formatDateTimeLocal(end);
                    }
                });
            }
        });
    </script>
}
```

Replace the entire `@section Scripts { ... }` with:

```javascript
@section Scripts {
    <script>
        function previewPhoto(input) {
            const preview = document
                .getElementById('photoPreview');
            const img = document
                .getElementById('previewImg');
            if (input.files && input.files[0]) {
                const reader = new FileReader();
                reader.onload = e => {
                    img.src = e.target.result;
                    preview.classList.remove('d-none');
                };
                reader.readAsDataURL(input.files[0]);
            } else {
                preview.classList.add('d-none');
            }
        }

        function toggleOnline() {
            const isOnline = document
                .getElementById('isOnlineSwitch')
                .checked;
            const meetingDiv = document
                .getElementById('meetingUrlDiv');
            meetingDiv.style.display =
                isOnline ? 'block' : 'none';
        }

        document.addEventListener('DOMContentLoaded', function () {

            function formatDateTimeLocal(date) {
                const year  = date.getFullYear();
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const day   = String(date.getDate()).padStart(2, '0');
                const hours = String(date.getHours()).padStart(2, '0');
                const mins  = String(date.getMinutes()).padStart(2, '0');
                return `${year}-${month}-${day}T${hours}:${mins}`;
            }

            var startField  = document.getElementById('startDateTime');
            var endField    = document.getElementById('endDateTime');
            var regDeadline = document.getElementById('regDeadline');

            // Compute min = now rounded up to next minute
            var nowMin = new Date();
            nowMin.setSeconds(0, 0);
            nowMin.setMinutes(nowMin.getMinutes() + 1);
            var nowStr = formatDateTimeLocal(nowMin);

            if (startField) startField.min = nowStr;
            if (endField)   endField.min   = (startField && startField.value) ? startField.value : nowStr;
            if (regDeadline) {
                regDeadline.min = nowStr;
                if (startField && startField.value)
                    regDeadline.max = startField.value;
            }

            // Set start to tomorrow at 9AM
            var tomorrow = new Date();
            tomorrow.setDate(tomorrow.getDate() + 1);
            tomorrow.setHours(9, 0, 0, 0);

            // Set end to tomorrow at 11AM
            var tomorrowEnd = new Date();
            tomorrowEnd.setDate(tomorrowEnd.getDate() + 1);
            tomorrowEnd.setHours(11, 0, 0, 0);

            // Only set if field is empty
            if (startField && !startField.value)
                startField.value = formatDateTimeLocal(tomorrow);

            if (endField && !endField.value)
                endField.value = formatDateTimeLocal(tomorrowEnd);

            // Re-apply min/max with the default values now set
            if (endField)    endField.min    = startField ? startField.value : nowStr;
            if (regDeadline && startField) regDeadline.max = startField.value;

            // Auto-update end + regDeadline max when start changes
            if (startField) {
                startField.addEventListener('change', function () {
                    if (this.value && endField) {
                        var start = new Date(this.value);
                        var end   = new Date(start);
                        end.setHours(end.getHours() + 2);
                        endField.value = formatDateTimeLocal(end);
                        endField.min   = this.value;
                    }
                    if (regDeadline) regDeadline.max = this.value;
                });
            }
        });
    </script>
}
```

- [ ] **Step 2: Verify manually**

Run the app:
```
dotnet run --project EduConnect.Web
```
Navigate to `https://localhost:7135/Event/Create` (must be logged in as Faculty/Dean/Chair Person).

Open the Start Date & Time picker — past dates/times should be grayed out and unselectable. Open the Registration Deadline picker — same behaviour. Change the start date forward; verify the Registration Deadline's max updates so you can't set a deadline after the event starts.

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Views/Event/Create.cshtml
git commit -m "feat: gray out past dates in event Create datetime pickers"
```

---

## Task 2: Create EventHub and Register Route

**Files:**
- Create: `EduConnect.Web/Hubs/EventHub.cs`
- Modify: `EduConnect.Web/Program.cs`

- [ ] **Step 1: Create `Hubs/EventHub.cs`**

```csharp
using Microsoft.AspNetCore.SignalR;

namespace EduConnect.Web.Hubs
{
    public class EventHub : Hub
    {
        public async Task JoinEvent(int eventId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId, $"event-{eventId}");
        }
    }
}
```

- [ ] **Step 2: Register the hub route in `Program.cs`**

Find these two lines near the bottom of `Program.cs`:

```csharp
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<GroupChatHub>("/groupChatHub");
```

Add the new hub directly after:

```csharp
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<GroupChatHub>("/groupChatHub");
app.MapHub<EventHub>("/eventHub");
```

- [ ] **Step 3: Verify the app still builds**

```
dotnet build EduConnect.Web
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Hubs/EventHub.cs EduConnect.Web/Program.cs
git commit -m "feat: add EventHub for real-time attendee count broadcasts"
```

---

## Task 3: Broadcast Count from EventController

**Files:**
- Modify: `EduConnect.Web/Controllers/EventController.cs`

The controller needs `IHubContext<EventHub>` injected, then two broadcast calls — one after a direct registration saves, one after a cancellation saves.

- [ ] **Step 1: Add using directives**

At the top of `EventController.cs`, find:

```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.IO;
```

Replace with:

```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Hubs;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.IO;
```

- [ ] **Step 2: Add the field and update the constructor**

Find the existing field declarations and constructor:

```csharp
private readonly ApplicationDbContext _context;
private readonly ILogger<EventController> _logger;
private readonly IWebHostEnvironment _environment;
private readonly IEmailService _emailService;
private readonly INotificationService _notificationService;

public EventController(
    ApplicationDbContext context,
    ILogger<EventController> logger,
    IWebHostEnvironment environment,
    IEmailService emailService,
    INotificationService notificationService)
{
    _context = context;
    _logger = logger;
    _environment = environment;
    _emailService = emailService;
    _notificationService = notificationService;
}
```

Replace with:

```csharp
private readonly ApplicationDbContext _context;
private readonly ILogger<EventController> _logger;
private readonly IWebHostEnvironment _environment;
private readonly IEmailService _emailService;
private readonly INotificationService _notificationService;
private readonly IHubContext<EventHub> _eventHub;

public EventController(
    ApplicationDbContext context,
    ILogger<EventController> logger,
    IWebHostEnvironment environment,
    IEmailService emailService,
    INotificationService notificationService,
    IHubContext<EventHub> eventHub)
{
    _context = context;
    _logger = logger;
    _environment = environment;
    _emailService = emailService;
    _notificationService = notificationService;
    _eventHub = eventHub;
}
```

- [ ] **Step 3: Broadcast after direct registration**

In the `Register` action, find the block that saves the QR code and sends the notification (around line 650–665):

```csharp
                registration.QRCode = qrCodePath;
                await _context.SaveChangesAsync();

                // Send real-time notification
                await _notificationService.SendAsync(
                    userID,
                    "EventRegistration",
                    $"You're registered for \"{ev.EventTitle}\"",
                    $"/Event/Details/{eventID}");
```

Add the broadcast immediately after the notification line:

```csharp
                registration.QRCode = qrCodePath;
                await _context.SaveChangesAsync();

                // Send real-time notification
                await _notificationService.SendAsync(
                    userID,
                    "EventRegistration",
                    $"You're registered for \"{ev.EventTitle}\"",
                    $"/Event/Details/{eventID}");

                // Broadcast updated attendee count to Details page viewers
                if (ev.MaxAttendees.HasValue)
                {
                    var newCount = await _context.EventRegistrations
                        .CountAsync(r => r.EventID == eventID && r.Status == "Registered");
                    await _eventHub.Clients
                        .Group($"event-{eventID}")
                        .SendAsync("UpdateAttendeeCount", newCount, ev.MaxAttendees.Value);
                }
```

- [ ] **Step 4: Broadcast after cancellation**

In the `CancelRegistration` action, find the line just before `TempData["Success"]` at the end of the `if (registration != null)` block:

```csharp
                TempData["Success"] =
                    "Registration cancelled. " +
                    "The next person on the " +
                    "waitlist has been notified.";
```

Add the broadcast immediately before that `TempData` line:

```csharp
                // Broadcast updated attendee count to Details page viewers
                var evMaxAtt = await _context.Events
                    .Where(e => e.EventID == eventID)
                    .Select(e => e.MaxAttendees)
                    .FirstOrDefaultAsync();
                if (evMaxAtt.HasValue)
                {
                    var cancelledCount = await _context.EventRegistrations
                        .CountAsync(r => r.EventID == eventID && r.Status == "Registered");
                    await _eventHub.Clients
                        .Group($"event-{eventID}")
                        .SendAsync("UpdateAttendeeCount", cancelledCount, evMaxAtt.Value);
                }

                TempData["Success"] =
                    "Registration cancelled. " +
                    "The next person on the " +
                    "waitlist has been notified.";
```

- [ ] **Step 5: Verify the app still builds**

```
dotnet build EduConnect.Web
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add EduConnect.Web/Controllers/EventController.cs
git commit -m "feat: broadcast attendee count via EventHub on register and cancel"
```

---

## Task 4: Wire Up Real-Time Counter in Details Page

**Files:**
- Modify: `EduConnect.Web/Views/Event/Details.cshtml`

Two sub-changes: add `id` attributes to the slot counter elements, then add the SignalR client in a new `@section Scripts` block.

- [ ] **Step 1: Add IDs to the slot counter span**

Find (inside the `@if (Model.MaxAttendees.HasValue)` block):

```razor
<span class="@(Model.IsFull ? "text-danger fw-bold" : "text-success fw-bold")">
    @Model.CurrentAttendees /
    @Model.MaxAttendees
</span>
```

Replace with:

```razor
<span id="slotCounter" class="@(Model.IsFull ? "text-danger fw-bold" : "text-success fw-bold")">
    @Model.CurrentAttendees / @Model.MaxAttendees
</span>
```

- [ ] **Step 2: Add ID to the progress bar**

Find:

```razor
<div class="progress-bar @(Model.IsFull ? "bg-danger" : "bg-success")"
     style="width: @pct%">
</div>
```

Replace with:

```razor
<div id="slotProgressBar"
     class="progress-bar @(Model.IsFull ? "bg-danger" : "bg-success")"
     style="width: @pct%">
</div>
```

- [ ] **Step 3: Consolidate the helper text into one element with an ID**

Find:

```razor
@if (!Model.IsFull)
{
    <small class="text-success">
        @Model.SlotsRemaining
        slots remaining
    </small>
}
else
{
    <small class="text-danger">
        Event is full
    </small>
}
```

Replace with:

```razor
<small id="slotHelperText" class="@(Model.IsFull ? "text-danger" : "text-success")">
    @(Model.IsFull ? "Event is full" : $"{Model.SlotsRemaining} slots remaining")
</small>
```

- [ ] **Step 4: Add `@section Scripts` with SignalR client**

At the very end of `Details.cshtml` (after the closing `</div>` of the main row), add:

```razor
@if (Model.MaxAttendees.HasValue)
{
    @section Scripts {
        <script>
            (function () {
                var eventID     = @Model.EventID;
                var maxAttendees = @Model.MaxAttendees.Value;

                var conn = new signalR.HubConnectionBuilder()
                    .withUrl("/eventHub")
                    .withAutomaticReconnect()
                    .build();

                conn.on("UpdateAttendeeCount", function (current, max) {
                    var isFull    = current >= max;
                    var remaining = max - current;
                    var pct       = max > 0 ? (current / max * 100) : 0;

                    var counter  = document.getElementById("slotCounter");
                    var bar      = document.getElementById("slotProgressBar");
                    var helper   = document.getElementById("slotHelperText");

                    if (!counter || !bar || !helper) return;

                    counter.textContent  = current + " / " + max;
                    bar.style.width      = pct + "%";

                    if (isFull) {
                        counter.className = "text-danger fw-bold";
                        bar.className     = "progress-bar bg-danger";
                        helper.textContent = "Event is full";
                        helper.className   = "text-danger";
                    } else {
                        counter.className = "text-success fw-bold";
                        bar.className     = "progress-bar bg-success";
                        helper.textContent = remaining + " slots remaining";
                        helper.className   = "text-success";
                    }
                });

                conn.start()
                    .then(function () { return conn.invoke("JoinEvent", eventID); })
                    .catch(function (err) { console.error("EventHub:", err); });
            })();
        </script>
    }
}
```

The `signalR` global is already available — `_Layout.cshtml` loads it from CDN for the existing notification system.

- [ ] **Step 5: Build and verify**

```
dotnet build EduConnect.Web
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Manual end-to-end test**

Run the app and open an event Details page for a capped event (one with Max Attendees set) in two browser tabs logged in as different users.

1. In tab 2, register for the event.
2. Observe tab 1 — the slot counter, progress bar, and helper text should update within a second without any page reload.
3. In tab 2, cancel the registration.
4. Observe tab 1 — the counter should decrement back.

For an unlimited event (no Max Attendees), verify no JS errors appear in the browser console.

- [ ] **Step 7: Commit**

```bash
git add EduConnect.Web/Views/Event/Details.cshtml
git commit -m "feat: real-time attendee count on event Details via EventHub"
```

---

## Self-Review

**Spec coverage:**
- [x] Past-date graying on all three datetime-local inputs (start, end, deadline) — Task 1
- [x] `regDeadline.max` tracks start so deadline can't be after event — Task 1
- [x] `EventHub` with `JoinEvent` — Task 2
- [x] `/eventHub` route registered — Task 2
- [x] Controller broadcasts after Register (direct path only, not waitlist) — Task 3
- [x] Controller broadcasts after CancelRegistration — Task 3
- [x] Details DOM IDs on counter, progress bar, helper text — Task 4
- [x] Details SignalR JS joins group and handles `UpdateAttendeeCount` — Task 4
- [x] Unlimited events skip broadcast and skip JS — Task 3 guard + Task 4 `@if`

**No placeholders:** All steps contain exact code.

**Type consistency:** `UpdateAttendeeCount` event name matches in controller (`SendAsync`) and client (`conn.on`). `JoinEvent` matches hub method name and client `conn.invoke`. Element IDs `slotCounter`, `slotProgressBar`, `slotHelperText` match between Razor and JS.
