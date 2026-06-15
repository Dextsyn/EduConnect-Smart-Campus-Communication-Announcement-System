# Chatbot Clickable Links for Announcements & Events

**Date:** 2026-06-15  
**Status:** Approved

## Summary

When the EduConnect chatbot mentions a specific announcement or event, its response should contain a clickable link that navigates the user directly to that item's detail page.

## Approach

Option A: Gemini emits markdown-style links (`[Title](/Announcement/Details/{id})`). The system prompt supplies IDs and a formatting rule. The frontend parses only whitelisted relative links and renders them as `<a>` tags.

## Changes

### 1. `ChatbotService.BuildSystemPromptAsync` (ChatbotService.cs)

**Formatting instruction** — add near the top of the system prompt, before the data section:

> When referencing a specific announcement, format it as a markdown link: `[Title](/Announcement/Details/{ID})`.  
> When referencing a specific event, format it as: `[Title](/Event/Details/{ID})`.  
> Use these exact path formats only. Do not use full URLs or any other format.

**Announcement lines** — include the `AnnouncementID`:

```
- [Category] Title | ID:12 (Published: Jun 10, 2026)
  Body excerpt…
```

**Event lines** — include the `EventID`:

```
- Event Title | ID:7 | Jun 20, 2026 10:00 AM | Room 301, 10 seats available
  Description excerpt…
```

### 2. `_ChatbotWidget.cshtml` — `appendMessage` function

Bot messages pass through a `renderBotText(text)` function before being set on the bubble:

1. HTML-escape the entire string (prevents XSS).
2. Find all `[text](url)` patterns where `url` starts with `/Announcement/Details/` or `/Event/Details/`.
3. Replace matched patterns with `<a href="{url}" class="chatbot-link" target="_blank">{escaped-text}</a>`.
4. Set `bubble.innerHTML` with the result.

User messages continue to use `bubble.textContent` (no parsing, fully escaped).

**Link styling** — `.chatbot-link` inside `.chat-bubble.bot`:

```css
.chat-bubble.bot a.chatbot-link {
    color: #0d6efd;
    text-decoration: underline;
    font-weight: 500;
}
.chat-bubble.bot a.chatbot-link:hover {
    color: #0a58ca;
}
```

## Security

- Only links whose URL matches `/Announcement/Details/` or `/Event/Details/` are converted. All other text (including any other markdown patterns Gemini may emit) is rendered as escaped plain text.
- The text portion of each matched link is also HTML-escaped before insertion.
- `target="_blank"` is used without `rel="noopener"` concern since these are same-origin relative URLs.

## Out of Scope

- Rendering other markdown (bold, lists, code blocks) — plain text only except for whitelisted links.
- Changing the controller response shape — still `{ response: string }`.
- Caching invalidation — the 5-minute prompt cache already handles re-fetching IDs on expiry.

## Files Affected

| File | Change |
|------|--------|
| `EduConnect.Web/Services/ChatbotService.cs` | Add IDs to prompt data; add link-formatting instruction |
| `EduConnect.Web/Views/Shared/_ChatbotWidget.cshtml` | Add `renderBotText`, update `appendMessage`, add `.chatbot-link` CSS |
