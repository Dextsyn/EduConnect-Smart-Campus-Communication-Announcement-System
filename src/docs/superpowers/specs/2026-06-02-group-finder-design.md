# Group Finder — Design Spec
**Date:** 2026-06-02
**Project:** EduConnect (Adamson University campus communication platform)

---

## Overview

A student-only group finder that lets students create and join small groups around shared academic topics or personal interests. Each group has a temporary in-group chat for coordinating meetups. Groups are public and browsable by category.

---

## Constraints & Rules

| Rule | Detail |
|------|--------|
| Who can create | Students only (`RoleName == "Student"`) |
| Who can join | Students only |
| Who can view browse page | All logged-in users; non-students see groups but cannot create or join |
| Max members | Set by creator, between 2 and 50 |
| Group privacy | All groups are public — anyone can browse and join open groups |
| Slot reopen | If a member leaves a Full group, one slot reopens and status reverts to Open; `ChatExpiresAt` is cleared |
| Chat expiry | When a group becomes Full, `ChatExpiresAt` is set to `UtcNow + 7 days`. After that the background service deletes all messages and sets status to `Dissolved` |
| Creator powers | Creator can dissolve their own group at any time (deletes all messages, sets `Dissolved`) |

---

## Categories

| Type | Values |
|------|--------|
| Academic | `Study Group`, `Project Team`, `Research` |
| Interest | `Hobby & Arts`, `Sports & Fitness`, `Tech & Gaming`, `Volunteer & Advocacy`, `Culture & Faith` |

---

## Data Models

### `Groups` table

```csharp
[Table("Groups")]
public class Group
{
    [Key] public int GroupID { get; set; }
    [Required] public int CreatorID { get; set; }
    [Required][MaxLength(150)] public string Name { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [Required][MaxLength(50)] public string Category { get; set; }
    public int MaxMembers { get; set; }             // 2–50
    [MaxLength(20)] public string Status { get; set; } = "Open"; // Open | Full | Dissolved
    public DateTime? ChatExpiresAt { get; set; }    // set when Full, cleared if slot reopens
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Creator { get; set; }
    public ICollection<GroupMember> Members { get; set; }
    public ICollection<GroupMessage> Messages { get; set; }
}
```

### `GroupMembers` table

```csharp
[Table("GroupMembers")]
public class GroupMember
{
    [Key] public int MembershipID { get; set; }
    [Required] public int GroupID { get; set; }
    [Required] public int UserID { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Group Group { get; set; }
    public User User { get; set; }
}
// Unique index: (GroupID, UserID)
```

### `GroupMessages` table

```csharp
[Table("GroupMessages")]
public class GroupMessage
{
    [Key] public int MessageID { get; set; }
    [Required] public int GroupID { get; set; }
    [Required] public int SenderID { get; set; }
    [Required][MaxLength(2000)] public string Content { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public Group Group { get; set; }
    public User Sender { get; set; }
}
```

### EF Core configuration (in `OnModelCreating`)

- `Groups`: PK on `GroupID`; FK `CreatorID → Users` with `DeleteBehavior.Restrict`
- `GroupMembers`: PK on `MembershipID`; unique index on `(GroupID, UserID)`; FKs to `Groups` and `Users` both `Restrict`
- `GroupMessages`: PK on `MessageID`; FKs to `Groups` (`Restrict`) and `Users` (`Restrict`)

---

## Backend

### `GroupController`

Session-auth helpers match the project pattern:
```csharp
private bool IsLoggedIn() => HttpContext.Session.GetString("UserID") != null;
private int GetUserID() => int.Parse(HttpContext.Session.GetString("UserID"));
private bool IsStudent() => HttpContext.Session.GetString("RoleName") == "Student";
```

| Action | Route | Auth | Description |
|--------|-------|------|-------------|
| `Index` | `GET /Group` | logged-in | Browse all non-dissolved groups. Passes full list to view; JS filters by category client-side. |
| `Create` GET | `GET /Group/Create` | student | Show create form |
| `Create` POST | `POST /Group/Create` | student | Validate and save new group; creator auto-joins as first member |
| `Details` | `GET /Group/Details/{id}` | logged-in | Group info + member list. If viewer is a member, also loads last 50 messages for initial render |
| `Join` | `POST /Group/Join/{id}` | student | Add member; if `MemberCount == MaxMembers` after join → `Status = Full`, `ChatExpiresAt = UtcNow + 7 days` |
| `Leave` | `POST /Group/Leave/{id}` | student | Remove member; if group was `Full` → `Status = Open`, `ChatExpiresAt = null`. Creator cannot leave (must dissolve). |
| `Dissolve` | `POST /Group/Dissolve/{id}` | creator only | Delete all `GroupMessages`, set `Status = Dissolved` |

### `GroupChatHub` (SignalR)

New hub in `EduConnect.Web/Hubs/GroupChatHub.cs`:

```csharp
public class GroupChatHub : Hub
{
    // OnConnectedAsync: reads groupId from query string,
    // verifies caller is a member of that group via DB,
    // then adds to SignalR group "group-{groupId}"

    // SendMessage(int groupId, string content):
    // - server-side membership check
    // - saves GroupMessage to DB
    // - broadcasts { senderName, content, sentAt } to "group-{groupId}"
}
```

Registered in `Program.cs`:
```csharp
app.MapHub<GroupChatHub>("/groupChatHub");
```

### `ChatExpiryService` (BackgroundService)

New file `Services/ChatExpiryService.cs`:
- Registered as a hosted service in `Program.cs`
- Runs every 60 minutes
- Queries: `Groups WHERE ChatExpiresAt <= UtcNow AND Status = 'Full'`
- For each matched group: bulk-deletes `GroupMessages`, sets `Status = Dissolved`

---

## Views

### `Views/Group/Index.cshtml`

```
┌─────────────────────────────────────────────────────┐
│  Group Finder                      [+ Create Group] │
│  ─────────────────────────────────────────────────  │
│  [All] [Study Group] [Project Team] [Research]      │
│  [Hobby & Arts] [Sports & Fitness] [Tech & Gaming]  │
│  [Volunteer & Advocacy] [Culture & Faith]           │
│  ─────────────────────────────────────────────────  │
│  🔍 Search groups...                                │
│                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────┐ │
│  │ Study Group  │  │ Tech & Gaming│  │ Research  │ │
│  │ DS101 Finals │  │ Indie Devs   │  │ Thesis AI │ │
│  │ 4/6 members  │  │ 8/8 members  │  │ 2/5 members│ │
│  │ [Join]       │  │ [Full]       │  │ [Join]    │ │
│  └──────────────┘  └──────────────┘  └───────────┘ │
└─────────────────────────────────────────────────────┘
```

- Category pill buttons filter cards client-side via JS (`data-category` attributes on cards)
- Search input filters by group name client-side
- `[+ Create Group]` button only renders when `RoleName == "Student"`
- Cards show: name, category badge, description snippet (truncated), member count `X/Y`, status badge, and a Join button (disabled/labeled "Full" when at capacity; hidden for non-students)

### `Views/Group/Create.cshtml`

Standard form:
- **Name** — text input, required, max 150 chars
- **Category** — `<select>` with the 8 category values grouped under Academic / Interest optgroups
- **Description** — textarea, optional, max 1000 chars
- **Max Members** — number input, min 2 max 50, required

### `Views/Group/Details.cshtml`

```
┌──────────────────────────────────────────────────────┐
│ ← Back   Indie Devs             [Tech & Gaming]      │
│ Created by Juan dela Cruz  •  8/8 members  [Full]    │
│ "A group for indie game developers to meet up..."    │
│ Members: [avatar] [avatar] [avatar] ...              │
│ ──────────────────────────────────────────────────   │
│  Chat  (expires in 5d 12h)                [Leave]   │
│ ┌────────────────────────────────────────────────┐  │
│ │ Juan: hey everyone when are we meeting?        │  │
│ │ Maria: how about Friday after class?           │  │
│ │                                                │  │
│ └────────────────────────────────────────────────┘  │
│ [Type a message...                    ] [Send]       │
└──────────────────────────────────────────────────────┘
```

States:
| Viewer | Chat panel shows |
|--------|-----------------|
| Member, group Open/Full | Full chat with message input |
| Non-member, group Open | "Join this group to access the chat" |
| Non-member, group Full | "This group is full" |
| Any viewer, group Dissolved | "This group's chat has expired" |

- Countdown `(expires in Xd Yh)` shown in chat header when `ChatExpiresAt` is set
- `[Leave]` button hidden for the creator (they must use `[Dissolve Group]`)
- `[Dissolve Group]` button shown only to creator, with a confirmation prompt
- Initial 50 messages loaded on page load; SignalR appends new messages in real time

---

## Navigation

Add "Group Finder" link to the sidebar/nav for all logged-in roles. Follows the same `_Layout.cshtml` pattern as Events and Announcements.

---

## Migration

One new EF Core migration: `AddGroupFinder`
- Creates `Groups`, `GroupMembers`, `GroupMessages` tables
- Does not touch existing `StudyGroups` or `StudyGroupMembers`

---

## Out of Scope

- Notifications when someone joins your group (can be added later)
- Image/file attachments in chat
- Group search by department tag
- Admin moderation UI for groups
