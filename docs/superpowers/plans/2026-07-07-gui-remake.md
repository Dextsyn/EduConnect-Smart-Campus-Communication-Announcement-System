# EduConnect GUI Remake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full visual restyle of EduConnect (ASP.NET Core 8 MVC) — Adamson navy/gold design system, modern campus-app look, and working role navigation on phone/tablet/laptop.

**Architecture:** Theme-first cascade. Task 1 replaces `site.css` with a design system that overrides Bootstrap 5 variables/classes so all ~45 views inherit the new skin immediately. Task 2 rewrites `_Layout.cshtml` (white topbar + navy sidebar + tablet drawer + phone bottom tab bar), extracting sidebar content into a shared partial used by both sidebar and drawer. Tasks 3–9 sweep views in priority order. Markup + CSS only — no controller, model, route, form-field, or JS behavior changes.

**Tech Stack:** ASP.NET Core 8 MVC, Razor, Bootstrap 5 (kept, incl. bootstrap.bundle.js), Bootstrap Icons, Plus Jakarta Sans (Google Fonts), Chart.js, SignalR client.

## Global Constraints

- Colors (exact): navy `#002F6C`, bright navy `#1B4A8F`, gold `#F2A900`, background `#F4F6FB`, surface `#FFFFFF`, text `#1A2233`, muted `#64748B`.
- Font: `'Plus Jakarta Sans'` with `'Segoe UI', system-ui, sans-serif` fallback.
- **Never change these IDs/attributes** (JS depends on them): `notif-toggle`, `notif-badge`, `notif-dropdown`, `notif-list`, `notif-mark-all`, `toast-container`, `body[data-user-id]`, all `chatbot-*` IDs, `eyeIcon`, Chart.js `<canvas>` IDs, QR scanner element IDs in `Event/Scanner.cshtml` & `Event/Scan.cshtml`, photo-preview element IDs in `Announcement/Create|Edit` and `Org/Post`.
- Never change: form field names/`asp-for` attributes, `asp-action`/`href` targets, `@model` directives, Razor logic (`@if` role checks etc.).
- Breakpoints: phone `<768px` (bottom tab bar), tablet `768–991px` (drawer via hamburger), laptop `≥992px` (fixed sidebar).
- Touch targets ≥44px on mobile; bottom bar uses `env(safe-area-inset-bottom)`.
- Build command (from repo root): `dotnet build src/EduConnect.Web` — must succeed after every task.
- Run for visual checks: `dotnet run --project src/EduConnect.Web` → `https://localhost:7135`. Verify at 375px, 768px, 1280px widths.
- There are no automated tests in this project. Each task's "test" is: build passes + visual/functional smoke check of the touched pages.
- Commit after every task with the message given in the task.

---

### Task 1: Design system CSS

**Files:**
- Modify (full replace): `src/EduConnect.Web/wwwroot/css/site.css`

**Interfaces:**
- Produces CSS classes consumed by all later tasks: `ec-topbar`, `ec-sidebar`, `ec-side-link`, `ec-side-heading`, `ec-drawer`, `ec-bottom-nav`, `ec-tab`, `ec-main`, `ec-hero`, `ec-stat-icon`, `ec-chip` (+ `ec-chip-academic|nonacademic|emergency|gold|neutral`), `ec-lift`, `ec-avatar-ring`, `ec-empty`, `ec-auth`, `ec-auth-panel`, `ec-auth-form`, `ec-user-card`.

- [ ] **Step 1: Replace `site.css` entirely with the design system**

```css
/* ═══ EduConnect Design System — Adamson navy & gold ═══ */

/* ── Tokens ── */
:root {
    --ec-navy: #002F6C;
    --ec-navy-bright: #1B4A8F;
    --ec-navy-deep: #00214D;
    --ec-gold: #F2A900;
    --ec-gold-soft: #FFF4DB;
    --ec-bg: #F4F6FB;
    --ec-surface: #FFFFFF;
    --ec-text: #1A2233;
    --ec-muted: #64748B;
    --ec-border: #E3E8F2;
    --ec-radius: 16px;
    --ec-radius-sm: 10px;
    --ec-shadow: 0 2px 8px rgba(16,38,84,.06), 0 8px 24px rgba(16,38,84,.07);
    --ec-shadow-lg: 0 12px 32px rgba(16,38,84,.16);
    --ec-topbar-h: 64px;
    --ec-sidebar-w: 264px;

    /* Bootstrap overrides */
    --bs-primary: var(--ec-navy);
    --bs-primary-rgb: 0, 47, 108;
    --bs-body-bg: var(--ec-bg);
    --bs-body-color: var(--ec-text);
    --bs-body-font-family: 'Plus Jakarta Sans', 'Segoe UI', system-ui, sans-serif;
    --bs-body-font-size: .9375rem;
    --bs-link-color: var(--ec-navy-bright);
    --bs-link-hover-color: var(--ec-navy);
    --bs-border-color: var(--ec-border);
}

body {
    background-color: var(--ec-bg);
    color: var(--ec-text);
    font-family: var(--bs-body-font-family);
}

h1, h2, h3, h4, h5, h6 { font-weight: 700; letter-spacing: -.01em; }

/* ── Bootstrap component re-skin (compiled colors need explicit overrides) ── */
.btn { border-radius: 12px; font-weight: 600; transition: transform .08s ease, box-shadow .15s ease; }
.btn:active { transform: scale(.97); }
.btn-primary {
    --bs-btn-bg: var(--ec-navy);
    --bs-btn-border-color: var(--ec-navy);
    --bs-btn-hover-bg: var(--ec-navy-bright);
    --bs-btn-hover-border-color: var(--ec-navy-bright);
    --bs-btn-active-bg: var(--ec-navy-deep);
    --bs-btn-active-border-color: var(--ec-navy-deep);
    --bs-btn-disabled-bg: #7C93B8;
    --bs-btn-disabled-border-color: #7C93B8;
}
.btn-outline-primary {
    --bs-btn-color: var(--ec-navy);
    --bs-btn-border-color: var(--ec-navy);
    --bs-btn-hover-bg: var(--ec-navy);
    --bs-btn-hover-border-color: var(--ec-navy);
    --bs-btn-active-bg: var(--ec-navy-deep);
    --bs-btn-active-border-color: var(--ec-navy-deep);
}
.btn-warning {
    --bs-btn-bg: var(--ec-gold);
    --bs-btn-border-color: var(--ec-gold);
    --bs-btn-color: #3A2B00;
    --bs-btn-hover-bg: #DB9900;
    --bs-btn-hover-border-color: #DB9900;
    --bs-btn-hover-color: #3A2B00;
}
.text-primary { color: var(--ec-navy) !important; }
.bg-primary { background-color: var(--ec-navy) !important; }
.text-bg-primary { background-color: var(--ec-navy) !important; color: #fff !important; }
.border-primary { border-color: var(--ec-navy) !important; }
.form-control, .form-select, .input-group-text { border-radius: 12px; border-color: var(--ec-border); }
.input-group > .form-control { border-radius: 0 12px 12px 0; }
.input-group > .input-group-text:first-child { border-radius: 12px 0 0 12px; background: #EEF2FA; color: var(--ec-muted); }
.form-control:focus, .form-select:focus {
    border-color: var(--ec-navy-bright);
    box-shadow: 0 0 0 .25rem rgba(27, 74, 143, .15);
}
.form-check-input:checked { background-color: var(--ec-navy); border-color: var(--ec-navy); }
.page-link { color: var(--ec-navy); border-radius: 10px; margin: 0 2px; border-color: var(--ec-border); }
.active > .page-link, .page-link.active { background-color: var(--ec-navy); border-color: var(--ec-navy); }
.nav-pills .nav-link { color: var(--ec-text); border-radius: var(--ec-radius-sm); }
.nav-pills .nav-link.active { background-color: var(--ec-navy); color: #fff; }
.dropdown-menu { border: 0; border-radius: 14px; box-shadow: var(--ec-shadow-lg); overflow: hidden; }
.alert { border: 0; border-radius: var(--ec-radius-sm); }
.modal-content { border: 0; border-radius: var(--ec-radius); box-shadow: var(--ec-shadow-lg); }
.badge { font-weight: 600; }

/* ── Cards ── */
.card {
    border: 0;
    border-radius: var(--ec-radius);
    box-shadow: var(--ec-shadow);
}
.card .card-header { background: transparent; border-bottom: 1px solid var(--ec-border); font-weight: 700; }
.ec-lift { transition: transform .18s ease, box-shadow .18s ease; }
.ec-lift:hover { transform: translateY(-3px); box-shadow: var(--ec-shadow-lg); }
.announcement-card { border: 0; border-radius: var(--ec-radius); box-shadow: var(--ec-shadow); transition: transform .18s ease, box-shadow .18s ease; }
.announcement-card:hover { transform: translateY(-3px); box-shadow: var(--ec-shadow-lg); }

/* ── Tables ── */
.table { --bs-table-hover-bg: #F0F4FC; }
.table thead th {
    background: #EEF2FA;
    color: var(--ec-muted);
    font-size: .75rem;
    text-transform: uppercase;
    letter-spacing: .06em;
    border-bottom: 0;
}
.table > :not(caption) > * > * { padding: .8rem .75rem; }

/* ── Topbar ── */
.ec-topbar {
    position: fixed;
    inset: 0 0 auto 0;
    height: var(--ec-topbar-h);
    background: var(--ec-surface);
    border-bottom: 1px solid var(--ec-border);
    z-index: 1040;
    display: flex;
    align-items: center;
    padding: 0 1rem;
}
.ec-topbar .ec-brand { color: var(--ec-navy); font-weight: 800; font-size: 1.2rem; text-decoration: none; letter-spacing: -.01em; }
.ec-topbar .ec-brand .bi { color: var(--ec-gold); }
.ec-burger { border: 0; background: transparent; font-size: 1.4rem; color: var(--ec-navy); padding: .4rem .6rem; }

/* ── Sidebar (laptop) ── */
.ec-sidebar {
    position: fixed;
    top: var(--ec-topbar-h);
    left: 0;
    bottom: 0;
    width: var(--ec-sidebar-w);
    background: linear-gradient(180deg, var(--ec-navy) 0%, var(--ec-navy-deep) 100%);
    color: #fff;
    overflow-y: auto;
    z-index: 1030;
    padding: 1.25rem 1rem;
}
.ec-side-heading {
    font-size: .68rem;
    text-transform: uppercase;
    letter-spacing: .12em;
    font-weight: 700;
    color: rgba(255,255,255,.45);
    padding: 0 .5rem;
    margin: 1.1rem 0 .4rem;
}
.ec-side-link {
    display: flex;
    align-items: center;
    gap: .65rem;
    color: rgba(255,255,255,.82);
    text-decoration: none;
    padding: .6rem .75rem;
    border-radius: var(--ec-radius-sm);
    font-weight: 500;
    font-size: .9rem;
    border-left: 3px solid transparent;
    min-height: 44px;
}
.ec-side-link:hover { background: rgba(255,255,255,.09); color: #fff; }
.ec-side-link.active {
    background: rgba(255,255,255,.12);
    color: #fff;
    border-left-color: var(--ec-gold);
}
.ec-side-link .bi { font-size: 1.05rem; width: 1.35rem; text-align: center; }
.ec-user-card {
    display: flex;
    align-items: center;
    gap: .75rem;
    background: rgba(255,255,255,.08);
    border-radius: var(--ec-radius-sm);
    padding: .75rem;
    color: #fff;
}
.ec-user-card small { color: rgba(255,255,255,.6); }
.ec-sidebar hr, .ec-drawer hr { border-color: rgba(255,255,255,.15); opacity: 1; }
.ec-avatar-ring { border: 2px solid var(--ec-gold); border-radius: 50%; }

/* ── Drawer (offcanvas reuses sidebar look) ── */
.ec-drawer {
    background: linear-gradient(180deg, var(--ec-navy) 0%, var(--ec-navy-deep) 100%);
    color: #fff;
    width: 290px !important;
}
.ec-drawer .btn-close { filter: invert(1); }

/* ── Main content shell ── */
.ec-main { padding: 1.5rem; min-height: calc(100vh - var(--ec-topbar-h)); margin-top: var(--ec-topbar-h); }
@media (min-width: 992px) {
    .ec-main { margin-left: var(--ec-sidebar-w); padding: 2rem; }
    .ec-footer { margin-left: var(--ec-sidebar-w); }
}

/* ── Bottom tab bar (phone) ── */
.ec-bottom-nav {
    position: fixed;
    inset: auto 0 0 0;
    background: var(--ec-surface);
    border-top: 1px solid var(--ec-border);
    display: flex;
    z-index: 1040;
    padding-bottom: env(safe-area-inset-bottom);
    box-shadow: 0 -4px 16px rgba(16,38,84,.06);
}
.ec-tab {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 2px;
    padding: .45rem 0 .4rem;
    min-height: 56px;
    color: var(--ec-muted);
    text-decoration: none;
    font-size: .66rem;
    font-weight: 600;
    border: 0;
    background: transparent;
    position: relative;
}
.ec-tab .bi { font-size: 1.25rem; }
.ec-tab.active { color: var(--ec-navy); }
.ec-tab.active::before {
    content: "";
    position: absolute;
    top: 0;
    width: 28px;
    height: 3px;
    border-radius: 0 0 3px 3px;
    background: var(--ec-gold);
}
@media (max-width: 767.98px) {
    body { padding-bottom: calc(64px + env(safe-area-inset-bottom)); }
    .ec-main { padding: 1rem; }
    #chatbot-toggle { bottom: calc(80px + env(safe-area-inset-bottom)) !important; }
    #chatbot-panel { bottom: calc(144px + env(safe-area-inset-bottom)) !important; right: 12px !important; width: calc(100vw - 24px) !important; }
}

/* ── Hero banner (dashboards) ── */
.ec-hero {
    background: linear-gradient(120deg, var(--ec-navy) 0%, var(--ec-navy-bright) 70%, #2A5DA8 100%);
    color: #fff;
    border-radius: var(--ec-radius);
    padding: 1.75rem 1.5rem;
    position: relative;
    overflow: hidden;
}
.ec-hero::after {
    content: "";
    position: absolute;
    right: -60px;
    top: -60px;
    width: 240px;
    height: 240px;
    border-radius: 50%;
    background: radial-gradient(circle, rgba(242,169,0,.35), transparent 70%);
}
.ec-hero .ec-hero-sub { color: rgba(255,255,255,.75); }

/* ── Stat cards ── */
.ec-stat-icon {
    width: 52px;
    height: 52px;
    border-radius: 14px;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 1.35rem;
    flex-shrink: 0;
}

/* ── Chips ── */
.ec-chip {
    display: inline-flex;
    align-items: center;
    gap: .3rem;
    font-size: .72rem;
    font-weight: 700;
    padding: .28rem .7rem;
    border-radius: 999px;
    line-height: 1;
}
.ec-chip-academic { background: #E3ECFB; color: var(--ec-navy); }
.ec-chip-nonacademic { background: #EFE7FB; color: #5B21B6; }
.ec-chip-emergency { background: #FDE5E5; color: #B42318; }
.ec-chip-gold { background: var(--ec-gold-soft); color: #8A5B00; }
.ec-chip-neutral { background: #EDF1F7; color: var(--ec-muted); }
.feed-badge { font-size: .7rem; padding: 4px 10px; border-radius: 20px; }

/* ── Empty state ── */
.ec-empty { text-align: center; padding: 3rem 1rem; color: var(--ec-muted); }
.ec-empty .bi { font-size: 2.6rem; display: block; margin-bottom: .75rem; color: #B9C4D8; }

/* ── Auth pages ── */
.ec-auth { min-height: 100vh; display: flex; background: var(--ec-bg); }
.ec-auth-panel {
    background: linear-gradient(150deg, var(--ec-navy) 0%, var(--ec-navy-deep) 100%);
    color: #fff;
    display: none;
    flex-direction: column;
    justify-content: center;
    padding: 3rem;
    position: relative;
    overflow: hidden;
}
.ec-auth-panel::after {
    content: "";
    position: absolute;
    left: -80px;
    bottom: -80px;
    width: 320px;
    height: 320px;
    border-radius: 50%;
    background: radial-gradient(circle, rgba(242,169,0,.3), transparent 70%);
}
.ec-auth-form { flex: 1; display: flex; align-items: center; justify-content: center; padding: 1.5rem; }
.ec-auth-form .card { width: 100%; max-width: 460px; }
@media (min-width: 992px) {
    .ec-auth-panel { display: flex; width: 44%; }
}
.ec-auth-logo {
    width: 64px; height: 64px;
    background: linear-gradient(135deg, var(--ec-navy), var(--ec-navy-bright));
    border-radius: 18px;
    display: flex; align-items: center; justify-content: center;
    box-shadow: var(--ec-shadow);
}

/* ── Legacy sidebar class kept harmless ── */
.sidebar .nav-link:hover { background: #e9ecef; border-radius: 8px; }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/EduConnect.Web`
Expected: Build succeeded.

- [ ] **Step 3: Visual sanity check**

Run app, open `https://localhost:7135` at 1280px. Expected: pages still render (old layout, new colors/fonts partially applied — full effect lands in Task 2). No console errors.

- [ ] **Step 4: Commit**

```bash
git add src/EduConnect.Web/wwwroot/css/site.css
git commit -m "feat(ui): add navy/gold design system overriding Bootstrap theme"
```

---

### Task 2: Layout rewrite — topbar, sidebar, drawer, bottom tab bar

**Files:**
- Create: `src/EduConnect.Web/Views/Shared/_SidebarContent.cshtml`
- Modify (full replace of body shell, preserve all listed IDs): `src/EduConnect.Web/Views/Shared/_Layout.cshtml`

**Interfaces:**
- Consumes: Task 1 classes.
- Produces: `_SidebarContent.cshtml` partial (user card + feed toggle + role nav + department tags) rendered inside both the desktop sidebar and the offcanvas drawer (`id="ecDrawer"`). All later tasks assume this layout shell.

- [ ] **Step 1: Create `_SidebarContent.cshtml`**

Move the ENTIRE current sidebar inner content of `_Layout.cshtml` (lines ~183–554: user info card, feed toggle, role-specific nav `@if/@else` chain, department tags) into the new partial, restyled. Keep every link href and every role branch exactly as today; only classes change:
- Wrapper user info → `ec-user-card`, avatar gets `ec-avatar-ring`, and the ui-avatars URL `background=0d6efd` → `background=002F6C`.
- Section labels (`Feed`, `Navigation`, `My Department`) → `<div class="ec-side-heading">…</div>`.
- Every `nav-link text-dark` → `ec-side-link` (drop `<ul>/<li>` wrappers; links can be direct children in a `d-flex flex-column gap-1` div).
- Feed toggle buttons: Academic → `btn btn-sm flex-fill btn-warning` (gold = active), Org Post → `btn btn-sm flex-fill btn-outline-light`.
- Department badges → `ec-chip ec-chip-gold` (CCIT) and `ec-chip-neutral` (School Wide) with inline style `background:rgba(255,255,255,.12);color:#fff` so they read on navy.
- At the bottom add (mobile-only escape hatch, since drawer replaces user dropdown there):

```html
<hr />
<a href="/Account/Profile" class="ec-side-link d-lg-none">
    <i class="bi bi-person"></i> Profile
</a>
<a href="/Account/Logout" class="ec-side-link d-lg-none" style="color:#FFB4A8;">
    <i class="bi bi-box-arrow-right"></i> Logout
</a>
```

- [ ] **Step 2: Rewrite `_Layout.cshtml` body shell**

Keep `<head>` as-is but add Google Fonts + theme-color before the site.css link:

```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet" />
<meta name="theme-color" content="#002F6C" />
```

New body structure (preserve `data-user-id`, notification IDs, toast container, chatbot partial, all script tags exactly as today):

```html
<body data-user-id="@Context.Session.GetString("UserID")">

    <!-- TOPBAR -->
    <header class="ec-topbar">
        <button class="ec-burger d-lg-none me-1" type="button"
                data-bs-toggle="offcanvas" data-bs-target="#ecDrawer"
                aria-label="Open menu">
            <i class="bi bi-list"></i>
        </button>
        <a class="ec-brand d-flex align-items-center gap-2" href="/">
            <i class="bi bi-broadcast"></i> EduConnect
        </a>
        <div class="ms-auto d-flex align-items-center gap-1">
            @if (Context.Session.GetString("UserID") != null)
            {
                <!-- Notification bell: SAME markup/IDs as today, but icon color classes
                     change from text-white to text-primary (topbar is white now) -->
                <div class="position-relative">
                    <button id="notif-toggle" type="button" class="btn position-relative px-2 border-0">
                        <i class="bi bi-bell fs-5 text-primary"></i>
                        <span id="notif-badge"
                              class="position-absolute top-0 start-100 translate-middle badge rounded-pill bg-danger d-none">0</span>
                    </button>
                    <div id="notif-dropdown" class="dropdown-menu dropdown-menu-end shadow p-0"
                         style="width:min(360px, calc(100vw - 24px));top:100%;right:0;position:absolute;">
                        <!-- inner content IDENTICAL to current: header row with notif-mark-all, notif-list div -->
                    </div>
                </div>
                <!-- Avatar dropdown: same items (role header, Profile, Logout), avatar img
                     gets class ec-avatar-ring and background=002F6C; name hidden on phones via d-none d-sm-inline -->
            }
            else
            {
                <a class="btn btn-primary btn-sm" href="/Account/Login">
                    <i class="bi bi-box-arrow-in-right me-1"></i> Login
                </a>
            }
        </div>
    </header>

    <!-- SIDEBAR (laptop) -->
    <aside class="ec-sidebar d-none d-lg-block">
        @await Html.PartialAsync("_SidebarContent")
    </aside>

    <!-- DRAWER (tablet hamburger + phone Menu tab) -->
    <div class="offcanvas offcanvas-start ec-drawer" tabindex="-1" id="ecDrawer" aria-label="Menu">
        <div class="offcanvas-header pb-0">
            <span class="fw-bold"><i class="bi bi-broadcast me-1" style="color:var(--ec-gold)"></i> EduConnect</span>
            <button type="button" class="btn-close" data-bs-dismiss="offcanvas" aria-label="Close"></button>
        </div>
        <div class="offcanvas-body pt-2">
            @await Html.PartialAsync("_SidebarContent")
        </div>
    </div>

    <!-- MAIN -->
    <main class="ec-main">
        @RenderBody()
    </main>

    <!-- FOOTER (unchanged text, add class ec-footer, hide on phones: d-none d-md-block) -->

    <!-- BOTTOM TAB BAR (phone only) -->
    <nav class="ec-bottom-nav d-md-none" aria-label="Primary">
        <a href="/" class="ec-tab"><i class="bi bi-house"></i>Home</a>
        <a href="/Announcement" class="ec-tab"><i class="bi bi-megaphone"></i>News</a>
        <a href="/Event" class="ec-tab"><i class="bi bi-calendar-event"></i>Events</a>
        <a href="/Notification" class="ec-tab"><i class="bi bi-bell"></i>Alerts</a>
        <button type="button" class="ec-tab" data-bs-toggle="offcanvas" data-bs-target="#ecDrawer">
            <i class="bi bi-grid"></i>Menu
        </button>
    </nav>

    <!-- scripts: bootstrap bundle, site.js, signalr, notifications.js, Scripts section,
         toast-container div, chatbot partial — ALL EXACTLY AS TODAY -->

    <!-- Active-nav highlighter (presentational only) -->
    <script>
        (function () {
            var path = location.pathname.toLowerCase().replace(/\/$/, '') || '/';
            document.querySelectorAll('.ec-side-link, .ec-tab[href]').forEach(function (a) {
                var href = (a.getAttribute('href') || '').toLowerCase().replace(/\/$/, '') || '/';
                if (href === path || (href !== '/' && path.startsWith(href))) a.classList.add('active');
            });
        })();
    </script>
</body>
```

Delete the old `<nav class="navbar …">`, the old `d-flex` wrapper with inline `margin-top:56px`, and the old inline-styled sidebar `<div class="sidebar …">`.

- [ ] **Step 3: Build**

Run: `dotnet build src/EduConnect.Web`
Expected: Build succeeded.

- [ ] **Step 4: Smoke check all three breakpoints**

Run app; log in as any user. Verify at 1280px (navy sidebar visible, white topbar, bell dropdown opens), 768px (hamburger opens drawer with full role nav), 375px (bottom tab bar visible, Menu opens drawer, content not covered by tab bar, chatbot button sits above tab bar). Verify notification bell still fetches/marks notifications (SignalR IDs untouched).

- [ ] **Step 5: Commit**

```bash
git add src/EduConnect.Web/Views/Shared/_Layout.cshtml src/EduConnect.Web/Views/Shared/_SidebarContent.cshtml
git commit -m "feat(ui): new responsive layout - navy sidebar, drawer, phone bottom tabs"
```

---

### Task 3: Auth pages (Login, Register, ForgotPassword, ResetPassword)

**Files:**
- Modify: `src/EduConnect.Web/Views/Account/Login.cshtml`
- Modify: `src/EduConnect.Web/Views/Account/Register.cshtml`
- Modify: `src/EduConnect.Web/Views/Account/ForgotPassword.cshtml`
- Modify: `src/EduConnect.Web/Views/Account/ResetPassword.cshtml`

**Interfaces:**
- Consumes: `ec-auth`, `ec-auth-panel`, `ec-auth-form`, `ec-auth-logo` from Task 1.

All four pages use `Layout = null` with their own `<head>`. For EACH page:

- [ ] **Step 1: Update each page's `<head>` and shell**

In `<head>`: add the Google Fonts links + `<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />` after the Bootstrap links; delete the page's inline `<style>` block (the old `#0d6efd` gradient styles).

Replace the body wrapper (`container/row/col` centering) with the split shell — Login example (same shell for all four; panel copy stays identical, only the form card content differs and is kept from the existing file unchanged, including `togglePassword()` script and `eyeIcon`):

```html
<body>
    <div class="ec-auth">
        <div class="ec-auth-panel">
            <div style="position:relative;z-index:1">
                <i class="bi bi-broadcast" style="font-size:2.5rem;color:var(--ec-gold)"></i>
                <h1 class="fw-bold mt-3" style="letter-spacing:-.02em">EduConnect</h1>
                <p class="fs-5" style="color:rgba(255,255,255,.8)">
                    Adamson University's smart campus communication system —
                    announcements, events, and organizations in one place.
                </p>
                <div class="d-flex flex-column gap-2 mt-4" style="color:rgba(255,255,255,.85)">
                    <div><i class="bi bi-megaphone me-2" style="color:var(--ec-gold)"></i> Campus announcements in real time</div>
                    <div><i class="bi bi-calendar-event me-2" style="color:var(--ec-gold)"></i> Event registration with QR check-in</div>
                    <div><i class="bi bi-people me-2" style="color:var(--ec-gold)"></i> Organizations and group finder</div>
                </div>
            </div>
        </div>
        <div class="ec-auth-form">
            <div class="card p-4 p-md-5">
                <div class="text-center mb-4 d-lg-none">
                    <div class="ec-auth-logo mx-auto mb-3"><i class="bi bi-broadcast text-white fs-3"></i></div>
                    <h4 class="fw-bold mb-0">EduConnect</h4>
                    <small class="text-muted">Smart Campus Communication System</small>
                </div>
                <h4 class="fw-bold d-none d-lg-block mb-1">Welcome back</h4>
                <p class="text-muted d-none d-lg-block mb-4">Sign in to your account</p>

                <!-- EXISTING alerts + form markup from the current file, UNCHANGED -->

            </div>
        </div>
    </div>
    <!-- existing scripts unchanged -->
</body>
```

Per-page heading text: Login "Welcome back / Sign in to your account"; Register "Create your account / Join the campus community"; ForgotPassword "Forgot password? / We'll email you a reset link"; ResetPassword "Reset password / Choose a new password".

- [ ] **Step 2: Build** — `dotnet build src/EduConnect.Web` → succeeded.
- [ ] **Step 3: Verify** — each page at 375px (panel hidden, card full width), 1280px (split screen). Login flow works; password eye toggle works; validation summary renders.
- [ ] **Step 4: Commit**

```bash
git add src/EduConnect.Web/Views/Account/
git commit -m "feat(ui): split-screen branded auth pages"
```

---

### Task 4: Dashboards (Home, Dean, Faculty, Admin, Staff)

**Files:**
- Modify: `src/EduConnect.Web/Views/Home/Index.cshtml`
- Modify: `src/EduConnect.Web/Views/Dean/Index.cshtml`
- Modify: `src/EduConnect.Web/Views/Faculty/Index.cshtml`
- Modify: `src/EduConnect.Web/Views/Admin/Index.cshtml`
- Modify: `src/EduConnect.Web/Views/Staff/Index.cshtml`

**Interfaces:**
- Consumes: `ec-hero`, `ec-stat-icon`, `ec-chip*`, `ec-empty`, `ec-lift`.

- [ ] **Step 1: Add hero banner to the top of each dashboard**

At the top of each dashboard view (above stat cards), insert:

```html
<div class="ec-hero mb-4">
    <div style="position:relative;z-index:1">
        <div class="ec-hero-sub small mb-1">@DateTime.Now.ToString("dddd, MMMM d, yyyy")</div>
        <h3 class="fw-bold mb-1">Welcome back, @(Context.Session.GetString("UserName") ?? "Guest") 👋</h3>
        <div class="ec-hero-sub">Here's what's happening on campus today.</div>
    </div>
</div>
```

(For `Staff/Index` say "Here are the latest safety reports."; for `Admin/Index` say "Here's your campus overview.")

- [ ] **Step 2: Restyle stat cards in each dashboard**

Existing pattern `div.rounded-circle.p-3.bg-*.bg-opacity-10 > i` → replace the wrapper with `ec-stat-icon` keeping the same tint classes, e.g.:

```html
<div class="ec-stat-icon bg-primary bg-opacity-10">
    <i class="bi bi-megaphone text-primary"></i>
</div>
```

Add `ec-lift` to each stat `card`. Keep all `@Model.*` bindings, role `@if` branches, grid columns (`col-6 col-md-3`) exactly as-is.

- [ ] **Step 3: Charts + lists polish**

In `Dean/Index` (and anywhere Chart.js renders): do NOT touch canvas IDs or data code; only wrap charts in standard `card` + `card-header`. Where a chart script defines colors, update color literals `#0d6efd`→`#002F6C` and any secondary series to `#F2A900` (data/label code untouched). Replace any "no data" text blocks with `ec-empty` pattern:

```html
<div class="ec-empty">
    <i class="bi bi-inbox"></i>
    No announcements yet
</div>
```

- [ ] **Step 4: Build** — `dotnet build src/EduConnect.Web` → succeeded.
- [ ] **Step 5: Verify** — each role's dashboard at 375/768/1280. Charts render, stats correct, hero shows name.
- [ ] **Step 6: Commit**

```bash
git add src/EduConnect.Web/Views/Home/ src/EduConnect.Web/Views/Dean/ src/EduConnect.Web/Views/Faculty/ src/EduConnect.Web/Views/Admin/Index.cshtml src/EduConnect.Web/Views/Staff/Index.cshtml
git commit -m "feat(ui): hero banners and restyled stat cards on all dashboards"
```

---

### Task 5: Announcement pages

**Files:**
- Modify: `src/EduConnect.Web/Views/Announcement/Index.cshtml`, `Details.cshtml`, `Create.cshtml`, `Edit.cshtml`, `MyAnnouncements.cshtml`, `ReviewQueue.cshtml`, `Review.cshtml`
- Modify: `src/EduConnect.Web/Views/Shared/_AnnouncementRows.cshtml`

**Interfaces:**
- Consumes: `ec-chip-academic|nonacademic|emergency|neutral`, `ec-lift`, `ec-empty`.

- [ ] **Step 1: Standardize page headers on every page in this task**

Existing header pattern (`h4.fw-bold` + `small.text-muted` + action button) is kept but: `h4` → `h3 class="fw-bold mb-1"`, icon keeps `text-primary`. Primary action button stays `btn btn-primary` (now navy).

- [ ] **Step 2: Feed-type chips**

Wherever a feed type or category badge renders (Index cards, `_AnnouncementRows`, Details, MyAnnouncements, ReviewQueue), map badge classes:
- Academic → `ec-chip ec-chip-academic`
- NonAcademic / Non-Academic → `ec-chip ec-chip-nonacademic`
- Emergency → `ec-chip ec-chip-emergency`
- department/category tags → `ec-chip ec-chip-neutral`

Razor pattern where the type is dynamic:

```csharp
@{ var chipClass = item.FeedType == "Emergency" ? "ec-chip-emergency"
       : item.FeedType == "NonAcademic" ? "ec-chip-nonacademic"
       : "ec-chip-academic"; }
<span class="ec-chip @chipClass">@item.FeedType</span>
```

- [ ] **Step 3: Cards & lists**

Announcement cards get `ec-lift`. Approval status badges: Pending → `ec-chip ec-chip-gold`, Approved → keep `badge text-bg-success`, Rejected → `badge text-bg-danger`. Empty lists → `ec-empty` block (icon `bi-megaphone`). ReviewQueue/Review approve/reject buttons keep their `asp-action`/form posts; approve = `btn btn-primary`, reject = `btn btn-outline-danger`.

- [ ] **Step 4: Create/Edit forms**

Wrap form in a single `card p-4`; group fields with existing markup; ensure the photo preview `<img>`/`<input>` IDs and the `URL.createObjectURL` script are byte-identical to current. Submit row: primary submit + `btn btn-outline-secondary` cancel; on phones make buttons full-width (`d-grid gap-2 d-sm-flex`).

- [ ] **Step 5: Build + verify** — build succeeds; check Index filters submit, pagination styled, Create photo preview works, Chair/Dean review flow renders at 375/1280.
- [ ] **Step 6: Commit**

```bash
git add src/EduConnect.Web/Views/Announcement/ src/EduConnect.Web/Views/Shared/_AnnouncementRows.cshtml
git commit -m "feat(ui): restyle announcement feed, forms, and review pages"
```

---

### Task 6: Event pages

**Files:**
- Modify: `src/EduConnect.Web/Views/Event/Index.cshtml`, `Details.cshtml`, `Create.cshtml`, `Edit.cshtml`, `Registrants.cshtml`, `Scan.cshtml`, `Scanner.cshtml`

**Interfaces:**
- Consumes: `ec-chip*`, `ec-lift`, `ec-empty`.

- [ ] **Step 1: Index** — filter tab buttons: active filter → `btn-primary` (navy) except "My Registrations" active stays `btn-success`; inactive → `btn-outline-secondary` (unchanged logic, only visual via theme). Event cards: add `ec-lift`; if a card shows a cover image, keep `<img>` as-is but add wrapper class `rounded-top overflow-hidden`; date/capacity move into a `d-flex gap-2` of `ec-chip ec-chip-neutral` chips; capacity/waitlist progress bars keep Bootstrap `progress` (navy fill via theme).
- [ ] **Step 2: Details** — top section becomes a `card overflow-hidden` with cover image full-bleed at top; register/cancel/waitlist buttons unchanged in behavior, `w-100` on phones via `d-grid d-sm-block`. QR code image (if shown) centered in a `card p-4 text-center`.
- [ ] **Step 3: Create/Edit/Registrants** — forms wrapped in `card p-4` (photo upload preview IDs untouched); Registrants table gets themed thead automatically; wrap table in `<div class="table-responsive">` if not already.
- [ ] **Step 4: Scanner/Scan** — do NOT touch any camera/scanner script or element IDs. Make the video/preview container full-width on phones: wrap in `card p-2 p-md-4` with `style="max-width:560px;margin:0 auto"`. Buttons ≥44px.
- [ ] **Step 5: Build + verify** — build; check list/calendar toggle, registration button, scanner opens camera on phone width, registrants table scrolls horizontally at 375px.
- [ ] **Step 6: Commit**

```bash
git add src/EduConnect.Web/Views/Event/
git commit -m "feat(ui): restyle event list, details, forms, and QR scanner shell"
```

---

### Task 7: Org and Group pages

**Files:**
- Modify: `src/EduConnect.Web/Views/Org/Index.cshtml`, `Details.cshtml`, `Create.cshtml`, `Edit.cshtml`, `Manage.cshtml`, `Post.cshtml`
- Modify: `src/EduConnect.Web/Views/Group/Index.cshtml`, `Create.cshtml`, `Details.cshtml`

- [ ] **Step 1: Org Index** — org cards get `ec-lift`; org logo/avatar images get `ec-avatar-ring`; member counts → `ec-chip ec-chip-neutral`.
- [ ] **Step 2: Org Details** — top header becomes a banner: `ec-hero mb-4` variant containing org name + description + join/leave buttons (buttons keep existing forms/handlers; inside hero use `btn-warning` for the primary join CTA so it pops gold on navy).
- [ ] **Step 3: Org Post** — form in `card p-4`; the photo preview `<img>` id and `URL.createObjectURL` script byte-identical to current (this page crashed once before — do not re-implement preview logic).
- [ ] **Step 4: Manage/Create/Edit + Group pages** — forms in `card p-4`; Group Finder cards `ec-lift` with tag chips `ec-chip-neutral`; empty states → `ec-empty` (icon `bi-people`).
- [ ] **Step 5: Build + verify** — build; check Org Post photo preview at 375px and 1280px, join/leave posts still work.
- [ ] **Step 6: Commit**

```bash
git add src/EduConnect.Web/Views/Org/ src/EduConnect.Web/Views/Group/
git commit -m "feat(ui): restyle organization and group finder pages"
```

---

### Task 8: Notifications, Safety, Admin CRUD, misc

**Files:**
- Modify: `src/EduConnect.Web/Views/Notification/Index.cshtml`
- Modify: `src/EduConnect.Web/Views/SafetyReport/Submit.cshtml`, `Confirmation.cshtml`
- Modify: `src/EduConnect.Web/Views/Staff/ReportDetails.cshtml`
- Modify: `src/EduConnect.Web/Views/Admin/PendingUsers.cshtml`, `Users.cshtml`, `AddUser.cshtml`, `EditUser.cshtml`
- Modify: `src/EduConnect.Web/Views/Home/Privacy.cshtml`, `src/EduConnect.Web/Views/Shared/Error.cshtml`

- [ ] **Step 1: Notification/Index** — render as timeline list: each notification a `card mb-2` with left icon in `ec-stat-icon` (small variant: add inline `style="width:40px;height:40px;font-size:1rem"`), unread items get `style="border-left:3px solid var(--ec-gold)"`. Mark-read buttons/links unchanged.
- [ ] **Step 2: SafetyReport Submit/Confirmation** — form in `card p-4` with a calm intro header (icon `bi-shield-exclamation text-primary`); Confirmation page centered `card p-5 text-center` with `bi-check-circle` success icon, `text-success`.
- [ ] **Step 3: Staff ReportDetails** — status workflow badges: New → `ec-chip ec-chip-gold`, In Progress → `ec-chip ec-chip-academic`, Resolved → `badge text-bg-success`. Action forms unchanged.
- [ ] **Step 4: Admin tables → responsive** — `Users.cshtml` and `PendingUsers.cshtml`: ensure tables are inside `<div class="table-responsive">`; action buttons get `btn-sm`; approve → `btn btn-primary btn-sm`, reject → `btn btn-outline-danger btn-sm`; delete-confirm modals keep their `aria-labelledby`/`aria-label` attributes (added in a recent accessibility commit — do not remove). PendingUsers: each pending user may instead render as a `card ec-lift` grid (`row g-3` of `col-12 col-md-6 col-xl-4`) with name/email/ID + approve/reject buttons — forms and asp-actions unchanged.
- [ ] **Step 5: AddUser/EditUser/Privacy/Error** — forms in `card p-4` (max-width 640px, `mx-auto`); Error page centered `ec-empty` with `bi-emoji-frown` + "Something went wrong" + link home.
- [ ] **Step 6: Build + verify** — build; verify Admin approve/reject posts work, tables usable at 375px, notification list renders.
- [ ] **Step 7: Commit**

```bash
git add src/EduConnect.Web/Views/Notification/ src/EduConnect.Web/Views/SafetyReport/ src/EduConnect.Web/Views/Staff/ src/EduConnect.Web/Views/Admin/ src/EduConnect.Web/Views/Home/Privacy.cshtml src/EduConnect.Web/Views/Shared/Error.cshtml
git commit -m "feat(ui): restyle notifications, safety reports, and admin pages"
```

---

### Task 9: Chatbot widget restyle

**Files:**
- Modify: `src/EduConnect.Web/Views/Shared/_ChatbotWidget.cshtml` (its `<style>` block only — no HTML/JS/ID changes)

- [ ] **Step 1: Re-theme the widget's `<style>` block**

In the widget's CSS only: `#chatbot-toggle` background → `linear-gradient(135deg, var(--ec-navy), var(--ec-navy-bright))`; `.chat-bubble.user` background `#0d6efd` → `var(--ec-navy)`; `.chat-bubble.bot a.chatbot-link` colors `#0d6efd/#0a58ca` → `var(--ec-navy-bright)/var(--ec-navy)`; panel header (if styled) navy with gold icon; `#chatbot-messages` background `#f8f9fa` → `var(--ec-bg)`. (Phone repositioning above the tab bar already handled by Task 1's media query.)

- [ ] **Step 2: Build + verify** — build; open chatbot at 375px (sits above bottom bar, panel fits viewport) and 1280px; send a message to confirm JS untouched.
- [ ] **Step 3: Commit**

```bash
git add src/EduConnect.Web/Views/Shared/_ChatbotWidget.cshtml
git commit -m "feat(ui): navy/gold chatbot widget theme"
```

---

### Task 10: Full cross-role, cross-device verification sweep

**Files:** none (verification only; fix-forward any issues found and amend into a `fix(ui):` commit)

- [ ] **Step 1: Build clean** — `dotnet build src/EduConnect.Web` → succeeded, 0 warnings introduced by our changes.
- [ ] **Step 2: Role sweep** — for EACH role (Administrator, Dean, Chair Person, Faculty, Staff, Student): log in, open every sidebar link, at 375px/768px/1280px. Confirm: role nav complete in drawer and sidebar, no horizontal overflow, tab bar never covers content or forms, notification bell badge updates, all TempData alerts render.
- [ ] **Step 3: Functional spot checks** — announcement create with photo preview; event registration + waitlist message; QR scanner camera opens; Dean charts render navy/gold; admin approve/reject; login/logout round trip.
- [ ] **Step 4: Grep for leftover old-brand colors**

Run: `grep -rn "0d6efd" src/EduConnect.Web/Views src/EduConnect.Web/wwwroot/css`
Expected: no matches (ui-avatars URLs now use 002F6C; auth inline styles removed).

- [ ] **Step 5: Final commit (if fixes were made)**

```bash
git add -A
git commit -m "fix(ui): responsive polish from cross-role verification sweep"
```
