# EduConnect GUI Remake — Design Spec

**Date:** 2026-07-07
**Status:** Approved by user
**Scope:** Full visual restyle of every view in `EduConnect.Web`. Markup + CSS only — no controller, model, route, form-field, or JavaScript behavior changes.

## Goals

1. Modern, distinctive campus look — "modern campus app" feel delivered as a website (not a literal native app).
2. First-class mobile experience: role-specific navigation must be fully reachable on phones (today the sidebar disappears below 992px).
3. Responsive across phone, tablet, and laptop breakpoints.
4. Every role keeps every feature it has today — restyle, never remove.

## Execution strategy: theme-first cascade

1. **Design system first** — new `site.css` design system overriding Bootstrap 5 CSS variables, so all ~45 views immediately inherit the new skin.
2. **Layout second** — rewrite `Views/Shared/_Layout.cshtml` (navbar, sidebar, mobile bottom tab bar, drawer).
3. **View sweep third** — restyle views in priority order: dashboards → feeds (announcements/events/orgs) → forms → admin tables → auth pages.

The app stays working and buildable at every step. Bootstrap 5 and its JS bundle are retained (dropdowns, collapse, offcanvas, modals).

## Design system

### Color tokens (CSS variables, mapped onto Bootstrap's `--bs-*` variables)

| Token | Value | Use |
|---|---|---|
| `--ec-navy` | `#002F6C` | Primary brand: sidebar, buttons, links |
| `--ec-navy-bright` | `#1B4A8F` | Hover states, gradients |
| `--ec-gold` | `#F2A900` | Accent: highlights, active nav indicator, badges, key CTAs |
| `--ec-bg` | `#F4F6FB` | Page background |
| `--ec-surface` | `#FFFFFF` | Cards, topbar |
| `--ec-text` | `#1A2233` | Body text |
| `--ec-muted` | `#64748B` | Secondary text |

Semantic colors (success/warning/danger/info) retained but re-tinted to harmonize with navy.

### Typography

"Plus Jakarta Sans" via Google Fonts, falling back to Segoe UI / system sans. Bolder, larger headings; 15px body.

### Components

- Cards: 16px radius, soft layered shadows, subtle hover lift.
- Pill chips for feed types and department tags, color-coded per feed type.
- Gradient navy hero banner on dashboards with user greeting.
- Gold-ring avatars.
- Rounded buttons with press animation.
- Tables: striped card-tables on desktop; collapse to stacked cards on phones.
- Consistent empty states (icon + message).

## Layout & responsive navigation

- **Laptop ≥992px:** full-height **navy sidebar** (role-specific links exactly as today: user card, feed toggle, nav, department tags), **white topbar** with page title, notification bell, avatar dropdown.
- **Tablet 768–991px:** hamburger in topbar opens a slide-in **drawer** (Bootstrap offcanvas) containing the full navy sidebar content.
- **Phone <768px:** fixed **bottom tab bar** — Home, Announcements, Events, Alerts (unread badge), Menu. Menu opens the drawer with full role nav, profile, logout. 44px+ touch targets, `env(safe-area-inset-*)` padding, main content bottom-padded so the tab bar never overlaps.
- Preserved JS hooks (IDs/attributes must not change): `notif-toggle`, `notif-badge`, `notif-dropdown`, `notif-list`, `notif-mark-all`, `data-user-id`, `toast-container`, chatbot widget markup, SignalR script includes, Chart.js include.

## Page-level redesign (feature parity per role)

Roles: Administrator, Dean, Chair Person, Faculty, Staff, Student (+ Student Pending, guest).

- **Shared:** announcement feed as modern cards (feed-type chips, department tags, author avatar, relative timestamps); event cards with cover-image header, date badge, capacity/waitlist progress bar; org pages with banner headers; notifications as grouped timeline; chatbot widget restyled navy/gold.
- **Auth (Login/Register/Forgot/Reset):** split-screen — navy brand panel + clean form; stacks vertically on phones.
- **Student:** dashboard (hero greeting + stat cards), feed, events (register/waitlist), group finder, safety report, notifications.
- **Faculty:** + create/edit announcement (photo preview via `URL.createObjectURL` kept intact), My Announcements, QR scanner (full-width camera on mobile).
- **Chair Person:** + review queue with approve/reject action cards.
- **Dean:** dashboard with Chart.js charts restyled to navy/gold palette, pending approvals.
- **Staff:** safety reports queue + report details with status workflow styling.
- **Administrator:** dashboard, pending-user verification cards (approve/reject), user management table (stacked cards on phones), add/edit user forms.

## Out of scope

- No new features, routes, or permissions.
- No dark mode.
- No framework migration (stays Bootstrap 5).
- No controller/model/database changes.

## Verification

- `dotnet build` after each batch of view changes.
- Run the app and smoke-test each role's pages at 375px (phone), 768px (tablet), 1280px (laptop).
- Functional checks on risky spots: notification bell + SignalR badge, photo preview on announcement/org post forms, QR scanner camera, Chart.js dashboards, event registration buttons, all form posts.
- No automated tests exist in this project; verification is visual + functional smoke-testing per role.
