# Announcement Create/Edit Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drop the FeedType dropdown from announcement forms, derive FeedType from the selected Category, simplify priority to Low/Medium/High pills, and add a toggle-controlled expiry date field that blocks past date selection.

**Architecture:** A `FeedType` column is added to `AnnouncementCategories`. Create/Edit POST actions look up the selected category and copy its `FeedType` onto the announcement — removing `FeedType` from the user-facing form entirely. `AnnouncementFormViewModel.FeedType` is deleted. Priority values 4 (Urgent) and 5 (Emergency) are retired from the UI; existing DB rows with those values are not touched. Expiry is controlled by a toggle switch that enables/disables a `datetime-local` input with `min` set to now.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core (SQL Server), Razor Views, Bootstrap 5, Vanilla JS

---

## Files Changed

| File | Action |
|---|---|
| `EduConnect.Web/Models/AnnouncementCategory.cs` | Add `FeedType` property |
| `EduConnect.Web/Migrations/<timestamp>_AddFeedTypeToCategories.cs` | Auto-generated; manually extend with UPDATE SQL |
| `EduConnect.Web/ViewModel/AnnouncementViewModel.cs` | Remove `FeedType` from `AnnouncementFormViewModel` |
| `EduConnect.Web/Controllers/AnnouncementController.cs` | Remove `model.FeedType` refs; derive from category |
| `EduConnect.Web/Views/Announcement/Create.cshtml` | Full sidebar rebuild |
| `EduConnect.Web/Views/Announcement/Edit.cshtml` | Full sidebar rebuild with pre-population |
| `../database/EduConnectDB.sql` | Add `FeedType` column + update seed INSERT |

---

## Task 1: Add FeedType to AnnouncementCategory model + run EF migration

**Files:**
- Modify: `EduConnect.Web/Models/AnnouncementCategory.cs`
- Create (auto): `EduConnect.Web/Migrations/<timestamp>_AddFeedTypeToCategories.cs`

- [ ] **Step 1: Add FeedType property**

In `EduConnect.Web/Models/AnnouncementCategory.cs`, add after `public bool IsEmergency { get; set; } = false;`:

```csharp
[Required]
[MaxLength(20)]
public string FeedType { get; set; } = "NonAcademic";
```

- [ ] **Step 2: Create the EF migration**

Run from `C:\EduConnect\src`:
```
dotnet ef migrations add AddFeedTypeToCategories --project EduConnect.Web
```

- [ ] **Step 3: Edit the generated migration to seed existing rows**

Open `EduConnect.Web/Migrations/<timestamp>_AddFeedTypeToCategories.cs`. In the `Up` method, after the `migrationBuilder.AddColumn` call, add:

```csharp
migrationBuilder.Sql(
    "UPDATE AnnouncementCategories SET FeedType = 'Academic' WHERE CategoryName = 'Academic'");
migrationBuilder.Sql(
    "UPDATE AnnouncementCategories SET FeedType = 'Emergency' WHERE CategoryName = 'Emergency'");
```

(All other categories keep the column default of `'NonAcademic'`.)

- [ ] **Step 4: Apply the migration**

```
dotnet ef database update --project EduConnect.Web
```

Expected: `Done.`

---

## Task 2: Remove FeedType from ViewModel + update AnnouncementController

**Files:**
- Modify: `EduConnect.Web/ViewModel/AnnouncementViewModel.cs`
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

Do all edits in this task before building — they are interdependent.

- [ ] **Step 1: Remove FeedType from AnnouncementFormViewModel**

In `EduConnect.Web/ViewModel/AnnouncementViewModel.cs`, delete these two lines from `AnnouncementFormViewModel`:

```csharp
[Required(ErrorMessage = "Feed type is required")]
public string FeedType { get; set; } = "Academic";
```

- [ ] **Step 2: Create GET — remove default FeedType assignment**

In `AnnouncementController.cs` `Create()` GET, delete the block that sets `model.FeedType` (currently inside the `else` branch, sets Academic/NonAcademic based on role):

```csharp
// DELETE these lines:
// Set default FeedType based on role
if (roleName == "Dean" ||
    roleName == "Chair Person")
    model.FeedType = "Academic";
else
    model.FeedType = "NonAcademic";
```

- [ ] **Step 3: Create POST — remove FeedType guard + derive from category**

In `Create` POST, change the Faculty strip block from:

```csharp
if (IsFaculty())
{
    model.IsEmergency = false;
    if (model.FeedType == "Emergency")
        model.FeedType = "Academic";
}
```

to:

```csharp
if (IsFaculty())
    model.IsEmergency = false;
```

Just before `var announcement = new Announcement`, add:

```csharp
var category = await _context.AnnouncementCategories
    .FindAsync(model.CategoryID);
```

Inside `new Announcement { ... }`, change:

```csharp
FeedType = model.FeedType,
```

to:

```csharp
FeedType = category?.FeedType ?? "NonAcademic",
```

- [ ] **Step 4: Edit GET — remove FeedType assignment**

In the `Edit` GET action, inside the `AnnouncementFormViewModel` initializer, delete:

```csharp
FeedType = announcement.FeedType,
```

- [ ] **Step 5: Edit POST — remove FeedType guard + derive from category**

In `Edit` POST, change the Faculty strip block from:

```csharp
if (IsFaculty())
{
    model.IsEmergency = false;
    if (model.FeedType == "Emergency")
        model.FeedType = "Academic";
}
```

to:

```csharp
if (IsFaculty())
    model.IsEmergency = false;
```

Just before `announcement.Title = model.Title;`, add:

```csharp
var category = await _context.AnnouncementCategories
    .FindAsync(model.CategoryID);
```

Change:

```csharp
announcement.FeedType = model.FeedType;
```

to:

```csharp
announcement.FeedType = category?.FeedType ?? "NonAcademic";
```

- [ ] **Step 6: Build to confirm no compile errors**

```
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Task 3: Rebuild Create.cshtml

**Files:**
- Modify: `EduConnect.Web/Views/Announcement/Create.cshtml`

- [ ] **Step 1: Replace the settings card body**

In `Create.cshtml`, find the `<div class="card-body">` inside the Settings card (the one containing Category, Feed Type, Priority, Expiry). Replace the entire contents of that `card-body` with:

```html
<div class="card-body">

    <!-- Category -->
    <div class="mb-3">
        <label class="form-label fw-semibold">
            Category <span class="text-danger">*</span>
        </label>
        <input type="hidden" id="CategoryID" name="CategoryID" value="" />
        <div class="cat-grid">
            @foreach (var cat in Model.Categories.Where(c =>
                c.CategoryName != "Emergency" || roleName == "Administrator"))
            {
                <div class="cat-card"
                     data-cat-id="@cat.CategoryID"
                     onclick="selectCategory(this, @cat.CategoryID)">
                    <div class="cat-dot"
                         style="background:@cat.ColorHex"></div>
                    <span>@cat.CategoryName</span>
                </div>
            }
        </div>
        <span asp-validation-for="CategoryID"
              class="text-danger small"></span>
    </div>

    <!-- Priority -->
    <div class="mb-3">
        <label class="form-label fw-semibold">Priority</label>
        <input type="hidden" id="Priority" name="Priority" value="1" />
        <div class="priority-group">
            <div class="priority-pill low active"
                 onclick="selectPriority(this, 1)">
                <div class="p-dot"></div>Low
            </div>
            <div class="priority-pill med"
                 onclick="selectPriority(this, 2)">
                <div class="p-dot"></div>Medium
            </div>
            <div class="priority-pill high"
                 onclick="selectPriority(this, 3)">
                <div class="p-dot"></div>High
            </div>
        </div>
    </div>

    <!-- Expiry Date -->
    <div class="mb-3">
        <div class="d-flex justify-content-between align-items-center mb-1">
            <label class="form-label fw-semibold mb-0">
                Expiry Date
            </label>
            <div class="form-check form-switch mb-0">
                <input class="form-check-input" type="checkbox"
                       role="switch" id="expiryToggle"
                       onchange="toggleExpiry(this)" />
                <label class="form-check-label text-muted small"
                       for="expiryToggle">Enable</label>
            </div>
        </div>
        <div id="expiryRow">
            <input asp-for="ExpiresAt" type="datetime-local"
                   class="form-control" id="expiryInput" disabled />
            <div class="form-text text-muted">
                <i class="bi bi-calendar-x me-1"></i>
                Past dates cannot be selected.
            </div>
        </div>
    </div>

    <!-- Emergency Toggle (Admin only) -->
    @if (roleName == "Administrator")
    {
        <div class="mb-1">
            <div class="form-check form-switch">
                <input asp-for="IsEmergency"
                       class="form-check-input"
                       type="checkbox"
                       role="switch" />
                <label asp-for="IsEmergency"
                       class="form-check-label fw-semibold text-danger">
                    Mark as Emergency
                </label>
            </div>
        </div>
    }

</div>
```

- [ ] **Step 2: Replace @section Scripts in Create.cshtml**

Replace the entire `@section Scripts { ... }` block with:

```razor
@section Scripts {
<style>
    .cat-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
    .cat-card {
        border: 2px solid #e5e7eb; border-radius: 8px;
        padding: 8px 10px; cursor: pointer;
        display: flex; align-items: center; gap: 8px;
        font-size: .82rem; font-weight: 500; background: #fff;
        transition: border-color .15s, background .15s;
        user-select: none;
    }
    .cat-card:hover, .cat-card.active { border-color: #6366f1; background: #eef2ff; }
    .cat-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .priority-group { display: flex; gap: 8px; }
    .priority-pill {
        flex: 1; padding: 8px 0; border-radius: 8px;
        border: 2px solid #dee2e6; background: #fff;
        cursor: pointer; text-align: center;
        font-size: .8rem; font-weight: 600;
        display: flex; flex-direction: column;
        align-items: center; gap: 4px; transition: all .15s;
        user-select: none;
    }
    .p-dot { width: 10px; height: 10px; border-radius: 50%; }
    .priority-pill.low  .p-dot { background: #22c55e; }
    .priority-pill.med  .p-dot { background: #f59e0b; }
    .priority-pill.high .p-dot { background: #ef4444; }
    .priority-pill.low.active  { border-color: #22c55e; background: #f0fdf4; color: #16a34a; }
    .priority-pill.med.active  { border-color: #f59e0b; background: #fffbeb; color: #d97706; }
    .priority-pill.high.active { border-color: #ef4444; background: #fef2f2; color: #dc2626; }
    #expiryInput:disabled { opacity: .45; }
</style>
<script>
    document.addEventListener('DOMContentLoaded', function () {
        // Auto-select first category card
        const first = document.querySelector('.cat-card');
        if (first) {
            first.classList.add('active');
            document.getElementById('CategoryID').value = first.dataset.catId;
        }

        // Set min date to now
        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        const local = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`;
        document.getElementById('expiryInput').min = local;
    });

    function toggleExpiry(cb) {
        const input = document.getElementById('expiryInput');
        input.disabled = !cb.checked;
        if (!cb.checked) input.value = '';
    }

    function selectCategory(el, id) {
        document.querySelectorAll('.cat-card').forEach(c => c.classList.remove('active'));
        el.classList.add('active');
        document.getElementById('CategoryID').value = id;
    }

    function selectPriority(el, val) {
        document.querySelectorAll('.priority-pill').forEach(p => p.classList.remove('active'));
        el.classList.add('active');
        document.getElementById('Priority').value = val;
    }

    function previewPhoto(input) {
        const preview = document.getElementById('photoPreview');
        const img = document.getElementById('previewImg');
        if (input.files && input.files[0]) {
            const reader = new FileReader();
            reader.onload = e => { img.src = e.target.result; preview.classList.remove('d-none'); };
            reader.readAsDataURL(input.files[0]);
        } else {
            preview.classList.add('d-none');
        }
    }
</script>
}
```

---

## Task 4: Rebuild Edit.cshtml

**Files:**
- Modify: `EduConnect.Web/Views/Announcement/Edit.cshtml`

- [ ] **Step 1: Replace the settings card body in Edit.cshtml**

Find the `<div class="card-body">` inside the Settings card. Replace its entire contents with:

```html
<div class="card-body">

    <!-- Category -->
    <div class="mb-3">
        <label class="form-label fw-semibold">
            Category <span class="text-danger">*</span>
        </label>
        <input type="hidden" id="CategoryID" name="CategoryID"
               value="@Model.CategoryID" />
        <div class="cat-grid">
            @foreach (var cat in Model.Categories.Where(c =>
                c.CategoryName != "Emergency" || roleName == "Administrator"))
            {
                <div class="cat-card @(Model.CategoryID == cat.CategoryID ? "active" : "")"
                     data-cat-id="@cat.CategoryID"
                     onclick="selectCategory(this, @cat.CategoryID)">
                    <div class="cat-dot"
                         style="background:@cat.ColorHex"></div>
                    <span>@cat.CategoryName</span>
                </div>
            }
        </div>
        <span asp-validation-for="CategoryID"
              class="text-danger small"></span>
    </div>

    <!-- Priority -->
    @{
        var displayPriority = (int)(Model.Priority > 3 ? 3 : Model.Priority);
    }
    <div class="mb-3">
        <label class="form-label fw-semibold">Priority</label>
        <input type="hidden" id="Priority" name="Priority"
               value="@displayPriority" />
        <div class="priority-group">
            <div class="priority-pill low @(displayPriority == 1 ? "active" : "")"
                 onclick="selectPriority(this, 1)">
                <div class="p-dot"></div>Low
            </div>
            <div class="priority-pill med @(displayPriority == 2 ? "active" : "")"
                 onclick="selectPriority(this, 2)">
                <div class="p-dot"></div>Medium
            </div>
            <div class="priority-pill high @(displayPriority == 3 ? "active" : "")"
                 onclick="selectPriority(this, 3)">
                <div class="p-dot"></div>High
            </div>
        </div>
    </div>

    <!-- Expiry Date -->
    @{
        var hasExpiry = Model.ExpiresAt.HasValue;
    }
    <div class="mb-3">
        <div class="d-flex justify-content-between align-items-center mb-1">
            <label class="form-label fw-semibold mb-0">
                Expiry Date
            </label>
            <div class="form-check form-switch mb-0">
                <input class="form-check-input" type="checkbox"
                       role="switch" id="expiryToggle"
                       @(hasExpiry ? "checked" : "")
                       onchange="toggleExpiry(this)" />
                <label class="form-check-label text-muted small"
                       for="expiryToggle">Enable</label>
            </div>
        </div>
        <div id="expiryRow">
            <input asp-for="ExpiresAt" type="datetime-local"
                   class="form-control" id="expiryInput"
                   @(!hasExpiry ? "disabled" : "") />
            <div class="form-text text-muted">
                <i class="bi bi-calendar-x me-1"></i>
                Past dates cannot be selected.
            </div>
        </div>
    </div>

    <!-- Emergency Toggle (Admin only) -->
    @if (roleName == "Administrator")
    {
        <div class="mb-1">
            <div class="form-check form-switch">
                <input asp-for="IsEmergency"
                       class="form-check-input"
                       type="checkbox"
                       role="switch" />
                <label asp-for="IsEmergency"
                       class="form-check-label fw-semibold text-danger">
                    Mark as Emergency
                </label>
            </div>
        </div>
    }

</div>
```

- [ ] **Step 2: Replace @section Scripts in Edit.cshtml**

Replace the entire `@section Scripts { ... }` block with:

```razor
@section Scripts {
<style>
    .cat-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
    .cat-card {
        border: 2px solid #e5e7eb; border-radius: 8px;
        padding: 8px 10px; cursor: pointer;
        display: flex; align-items: center; gap: 8px;
        font-size: .82rem; font-weight: 500; background: #fff;
        transition: border-color .15s, background .15s;
        user-select: none;
    }
    .cat-card:hover, .cat-card.active { border-color: #6366f1; background: #eef2ff; }
    .cat-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .priority-group { display: flex; gap: 8px; }
    .priority-pill {
        flex: 1; padding: 8px 0; border-radius: 8px;
        border: 2px solid #dee2e6; background: #fff;
        cursor: pointer; text-align: center;
        font-size: .8rem; font-weight: 600;
        display: flex; flex-direction: column;
        align-items: center; gap: 4px; transition: all .15s;
        user-select: none;
    }
    .p-dot { width: 10px; height: 10px; border-radius: 50%; }
    .priority-pill.low  .p-dot { background: #22c55e; }
    .priority-pill.med  .p-dot { background: #f59e0b; }
    .priority-pill.high .p-dot { background: #ef4444; }
    .priority-pill.low.active  { border-color: #22c55e; background: #f0fdf4; color: #16a34a; }
    .priority-pill.med.active  { border-color: #f59e0b; background: #fffbeb; color: #d97706; }
    .priority-pill.high.active { border-color: #ef4444; background: #fef2f2; color: #dc2626; }
    #expiryInput:disabled { opacity: .45; }
</style>
<script>
    document.addEventListener('DOMContentLoaded', function () {
        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        const local = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`;
        document.getElementById('expiryInput').min = local;
    });

    function toggleExpiry(cb) {
        const input = document.getElementById('expiryInput');
        input.disabled = !cb.checked;
        if (!cb.checked) input.value = '';
    }

    function selectCategory(el, id) {
        document.querySelectorAll('.cat-card').forEach(c => c.classList.remove('active'));
        el.classList.add('active');
        document.getElementById('CategoryID').value = id;
    }

    function selectPriority(el, val) {
        document.querySelectorAll('.priority-pill').forEach(p => p.classList.remove('active'));
        el.classList.add('active');
        document.getElementById('Priority').value = val;
    }

    function previewPhoto(input) {
        const preview = document.getElementById('photoPreview');
        const img = document.getElementById('previewImg');
        if (input.files && input.files[0]) {
            const reader = new FileReader();
            reader.onload = e => { img.src = e.target.result; preview.classList.remove('d-none'); };
            reader.readAsDataURL(input.files[0]);
        } else {
            preview.classList.add('d-none');
        }
    }

    function togglePhotoInput(checkbox) {
        const section = document.getElementById('photoInputSection');
        section.style.display = checkbox.checked ? 'none' : '';
    }
</script>
}
```

---

## Task 5: Update EduConnectDB.sql

**Files:**
- Modify: `../database/EduConnectDB.sql`

- [ ] **Step 1: Add FeedType column to CREATE TABLE AnnouncementCategories**

In the `CREATE TABLE AnnouncementCategories` block, add `FeedType` after `IsActive`:

```sql
FeedType        NVARCHAR(20)        NOT NULL    DEFAULT 'NonAcademic',
```

Also add inside the table's constraint list:

```sql
CONSTRAINT CHK_AnnouncementCategories_FeedType CHECK (
    FeedType IN ('Academic', 'NonAcademic', 'Emergency')
),
```

- [ ] **Step 2: Update the seed INSERT to include FeedType**

Replace the existing `INSERT INTO AnnouncementCategories` with:

```sql
INSERT INTO AnnouncementCategories (CategoryName, Description, ColorHex, IconName, IsEmergency, FeedType)
VALUES
    ('Academic',        'Class schedules, exams, grades',        '#3B82F6', 'fa-book',        0, 'Academic'),
    ('Extracurricular', 'Clubs, sports, campus events',          '#10B981', 'fa-star',        0, 'NonAcademic'),
    ('Administrative',  'School policies, office memos',         '#8B5CF6', 'fa-building',    0, 'NonAcademic'),
    ('Financial',       'Payments, tuition, billing',            '#F59E0B', 'fa-money-bill',  0, 'NonAcademic'),
    ('Health',          'Health advisories, clinic updates',     '#EF4444', 'fa-heart-pulse', 0, 'NonAcademic'),
    ('General',         'General campus information',            '#64748B', 'fa-info-circle', 0, 'NonAcademic'),
    ('Emergency',       'Urgent campus wide alerts',             '#DC2626', 'fa-exclamation', 1, 'Emergency');
```

---

## Task 6: Build + smoke test + commit

- [ ] **Step 1: Final build**

```
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Run the app**

```
dotnet run --project EduConnect.Web
```

Navigate to `https://localhost:7135/Announcement/Create` as Dean or Admin.

- [ ] **Step 3: Verify Create form**
- No FeedType dropdown visible
- Category card grid renders; first card auto-selected
- Clicking a category card highlights it and deselects others
- Priority pills show Low/Medium/High; Low active by default
- Expiry toggle OFF by default; datetime input is grayed (disabled)
- Flipping toggle ON enables the input; past times are not selectable
- Submitting → announcement saves with correct FeedType derived from the chosen category (verify in DB or by checking the announcement in the Index list filter)

- [ ] **Step 4: Verify Edit form**
- Editing an existing announcement: category card matching current CategoryID is pre-highlighted
- Priority pill matching current priority is active (any stored Priority > 3 shows as High)
- If announcement has ExpiresAt: toggle is ON, date is pre-filled
- If no ExpiresAt: toggle is OFF, input is grayed
- Saving → announcement updates correctly; FeedType re-derives from the (possibly changed) category

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Models/AnnouncementCategory.cs
git add EduConnect.Web/Migrations/
git add EduConnect.Web/ViewModel/AnnouncementViewModel.cs
git add EduConnect.Web/Controllers/AnnouncementController.cs
git add EduConnect.Web/Views/Announcement/Create.cshtml
git add EduConnect.Web/Views/Announcement/Edit.cshtml
git add ../database/EduConnectDB.sql
git commit -m "feat: derive FeedType from Category, add priority pills and expiry toggle"
```
