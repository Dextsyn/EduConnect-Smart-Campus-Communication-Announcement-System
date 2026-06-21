# Event: Past-Date Graying & Real-Time Attendee Count

**Date:** 2026-06-18  
**Status:** Approved

---

## Overview

Two UX improvements to the event system:

1. **Past-date graying** — Prevent misclicks on already-passed dates in the Create event form by setting `min` constraints on all three `datetime-local` inputs via JavaScript.
2. **Real-time attendee count** — The slot counter on the event Details page updates live (without a page reload) when any user registers or cancels, using SignalR.

---

## Feature 1: Past-Date Graying

### Scope

Only `Views/Event/Create.cshtml`. There is no Edit view for events. The three inputs affected:

| Input ID | Field | Constraint |
|---|---|---|
| `startDateTime` | Event start | `min = now` (rounded to next minute) |
| `endDateTime` | Event end | `min` follows current start value, updated on start change |
| `regDeadline` | Registration deadline | `min = now`, `max = current startDateTime value` |

### Implementation

All changes live inside the existing `DOMContentLoaded` block in `Create.cshtml`. No controller changes, no new files.

**Pseudocode:**
```
nowStr = formatDateTimeLocal(roundUpToNextMinute(new Date()))

startField.min = nowStr
regDeadline.min = nowStr
endField.min = startField.value || nowStr
regDeadline.max = startField.value || ""

on startField change:
    endField.min = startField.value
    regDeadline.max = startField.value
    (existing: auto-advance endField by 2 hours)
```

### Browser behavior

Chrome and Edge gray out and disable past values in the native datetime picker when `min` is set. Firefox marks out-of-range values as invalid (red outline) but does not gray the picker cells — acceptable, since server-side validation already rejects past start dates.

---

## Feature 2: Real-Time Attendee Count

### Scope

- New file: `Hubs/EventHub.cs`
- Modified: `Controllers/EventController.cs` (inject hub context, broadcast after register/cancel)
- Modified: `Views/Event/Details.cshtml` (add element IDs, add SignalR JS)
- Modified: `Program.cs` (map the new hub route)

The register/cancel **button** is not swapped in real-time. Only the counter display updates. Button state reflects page-load; users see an accurate live count without complex conditional button toggling.

### EventHub

```csharp
// Hubs/EventHub.cs
public class EventHub : Hub
{
    public async Task JoinEvent(int eventId)
    {
        await Groups.AddToGroupAsync(
            Context.ConnectionId, $"event-{eventId}");
    }
}
```

Clients call `JoinEvent(eventID)` immediately after connecting. The hub scopes broadcasts to `event-{id}` groups so only viewers of that specific event receive updates.

**Program.cs addition:**
```csharp
app.MapHub<EventHub>("/eventHub");
```

### Controller broadcast

`EventController` receives `IHubContext<EventHub>` via constructor injection.

Broadcast is fired after `SaveChangesAsync` in two actions:

**`Register` action** (both direct-register and waitlist branches):
```csharp
var newCount = await _context.EventRegistrations
    .CountAsync(r => r.EventID == eventID && r.Status == "Registered");

if (ev.MaxAttendees.HasValue)
    await _hubContext.Clients
        .Group($"event-{eventID}")
        .SendAsync("UpdateAttendeeCount", newCount, ev.MaxAttendees.Value);
```

**`CancelRegistration` action** (after status set to "Cancelled"):
```csharp
var newCount = await _context.EventRegistrations
    .CountAsync(r => r.EventID == eventID && r.Status == "Registered");

var evMaxAttendees = await _context.Events
    .Where(e => e.EventID == eventID)
    .Select(e => e.MaxAttendees)
    .FirstOrDefaultAsync();

if (evMaxAttendees.HasValue)
    await _hubContext.Clients
        .Group($"event-{eventID}")
        .SendAsync("UpdateAttendeeCount", newCount, evMaxAttendees.Value);
```

Broadcast is skipped for unlimited events (`MaxAttendees == null`) because there is no counter to display.

### Details page DOM changes

Add `id` attributes to the two elements that need live updates:

```html
<span id="slotCounter" class="...">
    @Model.CurrentAttendees / @Model.MaxAttendees
</span>

<small id="slotHelperText" class="...">
    @Model.SlotsRemaining slots remaining
    <!-- or: Event is full -->
</small>
```

The progress bar `<div>` already has an inline `style="width: @pct%"` — give it `id="slotProgressBar"`.

### Details page JS

Embed event data as JS variables via Razor (inside `@section Scripts`):

```js
const eventID = @Model.EventID;
const maxAttendees = @(Model.MaxAttendees.HasValue ? Model.MaxAttendees.Value.ToString() : "null");
```

SignalR connection:

```js
const eventConn = new signalR.HubConnectionBuilder()
    .withUrl("/eventHub")
    .withAutomaticReconnect()
    .build();

eventConn.on("UpdateAttendeeCount", (current, max) => {
    const isFull = current >= max;
    const remaining = max - current;
    const pct = max > 0 ? (current / max * 100) : 0;

    document.getElementById("slotCounter").textContent = `${current} / ${max}`;
    document.getElementById("slotProgressBar").style.width = `${pct}%`;

    const helper = document.getElementById("slotHelperText");
    if (isFull) {
        helper.textContent = "Event is full";
        helper.className = "text-danger";
        document.getElementById("slotProgressBar").className =
            "progress-bar bg-danger";
        document.getElementById("slotCounter").className =
            "text-danger fw-bold";
    } else {
        helper.textContent = `${remaining} slots remaining`;
        helper.className = "text-success";
        document.getElementById("slotProgressBar").className =
            "progress-bar bg-success";
        document.getElementById("slotCounter").className =
            "text-success fw-bold";
    }
});

eventConn.start().then(() => eventConn.invoke("JoinEvent", eventID));
```

The SignalR client script (`signalr.js`) is already loaded by the layout for the notification system — no additional script tag needed.

---

## Files Changed

| File | Change |
|---|---|
| `Hubs/EventHub.cs` | New file |
| `Program.cs` | Add `MapHub<EventHub>("/eventHub")` |
| `Controllers/EventController.cs` | Inject `IHubContext<EventHub>`, broadcast after Register + CancelRegistration |
| `Views/Event/Create.cshtml` | Add `min`/`max` JS inside existing DOMContentLoaded block |
| `Views/Event/Details.cshtml` | Add element IDs + SignalR JS in Scripts section |

---

## Out of Scope

- Edit event form (does not exist)
- Event listing/Index page counter updates
- Real-time button state changes (register/cancel button)
- Unlimited-capacity events (no counter shown, no broadcast needed)
