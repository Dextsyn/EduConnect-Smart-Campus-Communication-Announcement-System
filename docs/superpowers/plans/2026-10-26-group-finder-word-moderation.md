# Group Finder Word Moderation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mask English/Tagalog/Taglish bad words in Group Finder chat with `*`, block them in group names and descriptions, with an admin-editable word list that never lives in the public repo.

**Architecture:** A pure `ModerationMatcher` does all detection and masking from two word lists. `ContentModerationService` loads the lists from a new `ModerationWords` table and caches a built matcher in `IMemoryCache`. `GroupChatHub` masks and `GroupController.Create` blocks through that service. A startup importer fills the prohibited list once from the private `Moderation:SeedWords` setting, and a new admin page edits the table.

**Tech Stack:** ASP.NET Core 8 MVC + SignalR, EF Core (SQL Server; SQLite in tests), xUnit, Bootstrap 5.1 + `ec-*` styles.

**Spec:** `docs/superpowers/specs/2026-10-25-group-finder-word-moderation-design.md`

## Global Constraints

- **No real bad word anywhere in git**: not in code, migrations, tests, docs or commit messages. Tests use made-up words only (`zorbak`, `flimbo`, `zabb`, `ats`, phrase `flim bo mo`).
- Moderation runs on the server only (hub + controller), never only in JS.
- Masking: each matched span becomes `*` of the **same length**; everything outside matches is byte-for-byte unchanged.
- Group create error copy, exact: `Please remove inappropriate language from the group name or description.`
- Session auth, no `[Authorize]`; admin check is `RoleName == RoleNames.Administrator`.
- `main` deploys to Azure; work stays on branch `group-finder-moderation`; never push `main`.
- Run commands from `C:\EduConnect\src`. Tests: `dotnet test EduConnect.Tests`.

## Review Focus

1. Punctuation stuck to a word (`zorbak!`, `(zorbak)`, `zorbak,`) → the word is masked, the punctuation stays. *(Task 1 test `Punctuation_AroundWord_IsKept`.)*
2. Emoji or `ñ` next to a word (`zorbak😂`, `zorbak ñ`) → word masked, emoji/ñ untouched, no index-out-of-range on surrogate pairs. *(Task 1 test `EmojiAndEnye_AreUntouched`.)*
3. Empty word list (setting missing on a fresh Azure DB) → `Mask` returns the text unchanged and `ContainsProhibited` is false; nothing throws. *(Task 1 `EmptyLists_ChangeNothing`, Task 2 `Import_MissingSetting_DoesNothing`.)*
4. A message that is only bad words still sends, as all stars. *(Task 3 hub test `SendMessage_AllBadWords_SendsStars`.)*
5. Group create with a null description, or a bad word only in the description → no crash; blocked when bad. *(Task 3 tests.)*

---

### Task 1: `ModerationMatcher` (pure detection + masking)

**Files:**
- Create: `EduConnect.Web/Services/ModerationMatcher.cs`
- Test: `EduConnect.Tests/ModerationMatcherTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ModerationMatcher`
  - `public ModerationMatcher(IEnumerable<string> prohibited, IEnumerable<string> allowed)` — entries may be single words or phrases (`"flim bo mo"`); entries are lowercased, trimmed, inner whitespace collapsed.
  - `public static readonly ModerationMatcher Empty`
  - `public bool ContainsProhibited(string? text)`
  - `public string Mask(string text)`
  - `public static string NormalizeEntry(string word)` — trim, lowercase invariant, collapse whitespace to single spaces. Reused by Tasks 2 and 4.

- [ ] **Step 1: Write the failing tests** (`ModerationMatcherTests`, one `[Theory]`/`[Fact]` each). Matcher under test: prohibited `zorbak, flimbo, zabb, ats, kinazorbak, flim bo mo`; allowed `zorbaak`.

| Test | Input | Expected `Mask` |
|---|---|---|
| `CaseInsensitive` | `ZoRbAk mo` | `****** mo` |
| `WholeWordOnly` | `zorbaking class` | unchanged |
| `Substitutions` | `z0rb@k`, `zorb4k`, `@ts` | `******`, `******`, `***` |
| `StarWildcard` | `z*rbak` | `******` |
| `StretchedLetters` | `zorbaaaak`, `zooorbak` | `*********`, `********` |
| `CollapseNeverShortens` | `zab` (list has `zabb`) | unchanged; `zabbbb` → `******` |
| `DigitsOnlyNeverMatch` | `room 475` | unchanged |
| `SeparatorEvasion` | `z.o.r.b.a.k`, `z o r b a k`, `z-o-r-b-a-k`, `z_o_r_b_a_k` | 11 stars each |
| `SeparatorRunInsideSentence` | `hi z o r b a k ok` | `hi *********** ok` |
| `Phrase` | `flim bo mo talaga` / `flim  bo   mo` | `********** talaga` / 13 stars |
| `ExplicitAffixForm` | `kinazorbak` | `**********` |
| `AllowList_Wins` | `zorbaak` | unchanged (but `zorbaaak` → stars) |
| `Punctuation_AroundWord_IsKept` | `(zorbak), zorbak!` | `(******), ******!` |
| `EmojiAndEnye_AreUntouched` | `zorbak😂 niño` | `******😂 niño` |
| `EmptyLists_ChangeNothing` | `ModerationMatcher.Empty.Mask("zorbak")` | `zorbak`; `ContainsProhibited` false |
| `ContainsProhibited_MatchesMask` | each row above | `ContainsProhibited(x) == (Mask(x) != x)`; null/blank → false |

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test EduConnect.Tests --filter ModerationMatcherTests`
Expected: build FAIL, `ModerationMatcher` not found.

- [ ] **Step 3: Implement `ModerationMatcher`**

Algorithm (the one part the tests don't fully determine):

```
Tokenize(text): tokens = maximal runs of chars where char.IsLetterOrDigit(c) or c in "@$!|*";
  then trim leading/trailing '!', '|', '*' off each token (adjust span). Keep (Start, Length, Raw).
  Treat surrogate pairs/emoji as non-token chars (IsLetterOrDigit on the char is false for them).

Normalize(token): lowercase; if all digits -> return as-is (never substitute);
  else map 4,@->a  3->e  1,!,|->i  0->o  $,5->s  7->t ; '*' stays as wildcard.

TokenMatches(norm, word):
  if allowed contains raw-lowercase or norm -> false
  norm == word
  || (norm has '*' && norm.Length == word.Length && every non-'*' char equal)
  || (Collapse(norm) == Collapse(word) && norm.Length >= word.Length)   // Collapse = squash repeated letters
                                                                        // ">=" stops "zab" matching "zabb"

Find spans, in this order, skipping tokens already covered:
  1. Phrases (multi-token entries), longest first: consecutive tokens, each TokenMatches its part;
     only whitespace/punctuation between them. Span = first.Start .. last.End.
  2. Single-char runs: maximal runs of 1-char tokens separated only by ' ', '.', '-', '_' ;
     try every contiguous sub-run (longest first), join chars, Normalize, TokenMatches any single word.
     Span = first char .. last char (separators inside get starred).
  3. Every remaining token: TokenMatches any single word -> span = token.

Mask: copy text, overwrite each span index with '*'.
```

Store single-word entries in a `HashSet<string>` plus a precomputed collapse lookup; phrases as `string[]` lists. Build once in the constructor.

- [ ] **Step 4: Run tests until they pass**

Run: `dotnet test EduConnect.Tests --filter ModerationMatcherTests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Services/ModerationMatcher.cs EduConnect.Tests/ModerationMatcherTests.cs
git commit -m "Add word matcher for Group Finder moderation"
```

---

### Task 2: `ModerationWords` table, cached service, startup import

**Files:**
- Create: `EduConnect.Web/Models/ModerationWord.cs`
- Create: `EduConnect.Web/Services/IContentModerationService.cs`, `EduConnect.Web/Services/ContentModerationService.cs`
- Create: `EduConnect.Web/Services/ModerationWordImporter.cs`
- Modify: `EduConnect.Web/Data/ApplicationDbContext.cs` (DbSet + unique index on `Word`)
- Modify: `EduConnect.Web/Program.cs` (register service after line 23; run importer after `var app = builder.Build();`)
- Create: migration `AddModerationWords` (generated)
- Test: `EduConnect.Tests/ContentModerationServiceTests.cs`

**Interfaces:**
- Consumes: `ModerationMatcher(prohibited, allowed)`, `ModerationMatcher.NormalizeEntry`.
- Produces:
  - `[Table("ModerationWords")] public class ModerationWord { int ModerationWordID; [Required, MaxLength(100)] string Word; [MaxLength(20)] string? Language; bool IsAllowed; DateTime CreatedAt = DateTime.UtcNow; }` — `Language` is `English|Tagalog|Taglish`, null for imported words (shown as "—"). *(Plan's call: the setting carries no language.)*
  - `public static class ModerationLanguages { English, Tagalog, Taglish; All }` in the model file.
  - `public interface IContentModerationService { Task<bool> ContainsProhibitedAsync(string? text); Task<string> MaskAsync(string text); void InvalidateCache(); }`
  - `ContentModerationService(ApplicationDbContext context, IMemoryCache cache)`; cache key `"ModerationMatcher"`, no expiry; `InvalidateCache()` removes it.
  - `public static class ModerationWordImporter { public const string SettingKey = "Moderation:SeedWords"; public static Task<int> ImportAsync(ApplicationDbContext db, IConfiguration config, ILogger logger); }` returns rows added.

- [ ] **Step 1: Write the failing tests** (`ContentModerationServiceTests`, `TestDb` + `new MemoryCache(new MemoryCacheOptions())`)

- `Mask_UsesProhibitedRows_AndAllowedRows` — rows `zorbak` (prohibited), `zorbaak` (allowed): `MaskAsync("zorbak zorbaak")` == `"****** zorbaak"`.
- `Cache_IsStale_UntilInvalidated` — mask once, add row `flimbo`, `MaskAsync("flimbo")` still `"flimbo"`; after `InvalidateCache()` → `"******"`.
- `Import_AddsWords_WhenNoProhibitedRows` — config `Moderation:SeedWords` = `"zorbak, Flimbo\nflim  bo mo\n\n zorbak "` → returns 3; rows `zorbak`, `flimbo`, `flim bo mo`, all `IsAllowed == false`, `Language == null`.
- `Import_Skips_WhenProhibitedRowsExist` — existing prohibited row `zabb`; setting `zorbak` → returns 0, no `zorbak` row.
- `Import_IgnoresAllowListRows_WhenCounting` — only an allowed row `zorbaak` exists → import still runs; a seed word equal to an existing allowed word is skipped (no unique-index crash).
- `Import_MissingSetting_DoesNothing` — empty config → returns 0, no exception, logger got one warning (use a small list-backed `ILogger`; assert the warning text contains no seed words).

Config in tests: `new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{ ["Moderation:SeedWords"] = ... }).Build()`.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test EduConnect.Tests --filter ContentModerationServiceTests`
Expected: build FAIL.

- [ ] **Step 3: Implement model, DbSet, service, importer**

Importer splits on `\r`, `\n`, `,`; `NormalizeEntry`; drops blanks and >100 chars; distinct; skips words already in the table. "No prohibited words" = `!db.ModerationWords.Any(w => !w.IsAllowed)`. Missing/blank setting → `logger.LogWarning("Moderation:SeedWords is not set; no moderation words were imported.")`.

- [ ] **Step 4: Register and wire startup** in `Program.cs`

`builder.Services.AddScoped<EduConnect.Web.Services.IContentModerationService, EduConnect.Web.Services.ContentModerationService>();` next to the other services. After `var app = builder.Build();`: create a scope, resolve `ApplicationDbContext` and `ILogger<Program>`, `await ModerationWordImporter.ImportAsync(db, app.Configuration, logger);`.

- [ ] **Step 5: Add the migration with the allow-list seed**

Run: `dotnet ef migrations add AddModerationWords --project EduConnect.Web`
Then add `migrationBuilder.InsertData` for allowed rows (`IsAllowed = true`, `Language` set, fixed `CreatedAt = new DateTime(2026, 10, 26)`): English `class`, `classes`, `assignment`, `assess`, `assessment`, `passing`, `scunthorpe`; Tagalog `pukyutan`, `gagamba`, `tangkad`, `pakialam`, `titser`. **No prohibited words in the migration.** Mirror the seed with `HasData` in `ApplicationDbContext` only if that is how the project's other migration seeds are kept consistent; otherwise `InsertData` alone.

- [ ] **Step 6: Run tests**

Run: `dotnet test EduConnect.Tests`
Expected: all PASS (whole suite: the model change must not break `EnsureCreated`).

- [ ] **Step 7: Commit**

```bash
git add EduConnect.Web/Models/ModerationWord.cs EduConnect.Web/Services/IContentModerationService.cs EduConnect.Web/Services/ContentModerationService.cs EduConnect.Web/Services/ModerationWordImporter.cs EduConnect.Web/Data/ApplicationDbContext.cs EduConnect.Web/Program.cs EduConnect.Web/Migrations EduConnect.Tests/ContentModerationServiceTests.cs
git commit -m "Store moderation words and import the private starting list"
```

---

### Task 3: Moderate group create and group chat

**Files:**
- Modify: `EduConnect.Web/Controllers/GroupController.cs` (ctor; `Create` POST at ~line 90)
- Modify: `EduConnect.Web/Views/Group/Create.cshtml` (repopulate inputs)
- Modify: `EduConnect.Web/Hubs/GroupChatHub.cs` (ctor; `SendMessage`)
- Modify: `EduConnect.Tests/GroupExpiryTests.cs:38` (new ctor arg)
- Modify: `EduConnect.Tests/ControllerFakes.cs` (hub fakes)
- Test: `EduConnect.Tests/GroupModerationTests.cs`

**Interfaces:**
- Consumes: `IContentModerationService` (Task 2). Tests build the real `ContentModerationService` over `TestDb` with a seeded `zorbak` row.
- Produces:
  - `GroupController(ApplicationDbContext context, IContentModerationService moderation)`
  - `GroupChatHub(ApplicationDbContext context, IContentModerationService moderation)`
  - Create view reads `ViewData["Name"]`, `ViewData["Category"]`, `ViewData["Description"]`, `ViewData["MaxMembers"]` when present.
  - Test fakes: `FakeHubCallerContext : HubCallerContext` (Features holds an `IHttpContextFeature` whose `HttpContext` is `FakeSession.HttpContextWith(session)`); `FakeHubClients : IHubCallerClients` whose `Group(name)` returns a recording `IClientProxy` exposing `List<(string Method, object?[] Args)> Calls`. Other members may throw `NotSupportedException`.

- [ ] **Step 1: Write the failing tests** (`GroupModerationTests`)

- `Create_BadName_IsBlocked_AndKeepsInput` — name `Zorbak squad`, category `Study Group`, description `notes`, max 8 → `ViewResult`; `TempData["Error"]` == the exact copy; `ViewData["Name"] == "Zorbak squad"`, `ViewData["MaxMembers"] == 8`; `Groups` table empty.
- `Create_BadDescription_IsBlocked` — clean name, description `z.o.r.b.a.k` → blocked, no group.
- `Create_NullDescription_CleanName_Works` — redirect to `Details`, group saved.
- `SendMessage_MasksBeforeSaveAndBroadcast` — member sends `ang zorbak mo` → saved `Content == "ang ****** mo"`; broadcast `ReceiveMessage` payload's `content` == same.
- `SendMessage_AllBadWords_SendsStars` — `zorbak flimbo` (both rows seeded) → saved `"****** ******"`.
- `SendMessage_CleanText_Unchanged` — `see you at 3pm!` saved exactly.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test EduConnect.Tests --filter GroupModerationTests`
Expected: build FAIL (ctor arity).

- [ ] **Step 3: Implement**

- `Create`: after the category check, `if (await _moderation.ContainsProhibitedAsync(name) || await _moderation.ContainsProhibitedAsync(description))` → set the error and return `View()`. Before **every** `return View()` in the POST, set the four `ViewData` values (small private helper `KeepInput(name, category, description, maxMembers)`).
- `Create.cshtml`: `value="@ViewData["Name"]"`, textarea body `@ViewData["Description"]`, `maxMembers` value falls back to `10`, the matching `<option>` gets `selected` (and the placeholder loses `selected` when a category is kept).
- Hub: after the membership checks, `Content = await _moderation.MaskAsync(content.Trim())`.
- Update `GroupExpiryTests.As` to pass a `ContentModerationService` over `_db.Context` and a new `MemoryCache`.

- [ ] **Step 4: Run tests**

Run: `dotnet test EduConnect.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Controllers/GroupController.cs EduConnect.Web/Views/Group/Create.cshtml EduConnect.Web/Hubs/GroupChatHub.cs EduConnect.Tests/GroupExpiryTests.cs EduConnect.Tests/ControllerFakes.cs EduConnect.Tests/GroupModerationTests.cs
git commit -m "Mask bad words in group chat and block them in new groups"
```

---

### Task 4: Admin "Moderation Words" page

**Files:**
- Create: `EduConnect.Web/Controllers/ModerationWordsController.cs`
- Create: `EduConnect.Web/ViewModels/ModerationWordsViewModel.cs`
- Create: `EduConnect.Web/Views/ModerationWords/Index.cshtml`
- Modify: `EduConnect.Web/Services/IAuditService.cs` (`AuditArea.Moderation = "Moderation"`, add to `All`)
- Modify: `EduConnect.Web/Views/Shared/_SidebarContent.cshtml` (link after Audit Log, icon `bi-shield-exclamation`, text `Moderation Words`)
- Test: `EduConnect.Tests/ModerationWordsAdminTests.cs`

**Interfaces:**
- Consumes: `ModerationWord`, `ModerationLanguages`, `ModerationMatcher.NormalizeEntry`, `IContentModerationService.InvalidateCache()`, `IAuditService.Record`.
- Produces:
  - `[Route("Admin/ModerationWords")] public class ModerationWordsController(ApplicationDbContext context, IContentModerationService moderation, IAuditService audit)` — a separate controller so `AdminController`'s constructor (used by three test files) stays as is. *(Plan's call; URL matches the spec.)*
  - `[HttpGet("")] Task<IActionResult> Index()` → `ModerationWordsViewModel { List<ModerationWord> Prohibited; List<ModerationWord> Allowed; }`, each sorted by `Word`.
  - `[HttpPost("Add"), ValidateAntiForgeryToken] Task<IActionResult> Add(string word, string? language, bool isAllowed)`
  - `[HttpPost("Remove/{id:int}"), ValidateAntiForgeryToken] Task<IActionResult> Remove(int id)`
  - Non-admin → `Forbid()` (logged-out → redirect to `Account/Login`, as `AdminController` does).

- [ ] **Step 1: Write the failing tests** (`ModerationWordsAdminTests`; session RoleName Administrator unless stated; real `AuditService` as in `AuditLogTests`)

- `NonAdmin_IsRefused` — Student session → `Index`, `Add`, `Remove` all return `ForbidResult`.
- `Add_NormalizesAndSaves` — `Add("  ZorBak ", "Tagalog", false)` → row `zorbak`, `Language == "Tagalog"`, audit row with action `Add`, area `Moderation`; redirect to `Index`.
- `Add_RejectsBlankDuplicateAndBadLanguage` — `""`, existing `zorbak`, language `"French"` → no new row, `TempData["Error"]` set (`Enter a word.` / `That word is already on the list.` / `Choose English, Tagalog or Taglish.`).
- `Add_IsCaughtOnNextMessage` — mask `flimbo` via the same `ContentModerationService` instance (warms cache), `Add("flimbo", "English", false)`, then `MaskAsync("flimbo")` == `"******"`.
- `Remove_DeletesAuditsAndInvalidates` — row removed, audit action `Remove`, next `MaskAsync` no longer masks it.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test EduConnect.Tests --filter ModerationWordsAdminTests`
Expected: build FAIL.

- [ ] **Step 3: Implement controller, view model, view, audit area, sidebar link**

Summaries: `Added "{word}" to the moderation list` / `Removed "{word}" from the moderation list` (the audit log is admin-only and lives in the DB, not the repo). Call `InvalidateCache()` after each successful `SaveChangesAsync`. View follows `Views/Admin/AuditLog.cshtml`: page header with icon + Back to `/Admin`, `TempData` alerts, an add form (word input `maxlength="100"`, language select, Prohibited/Allowed radio), then two cards "Blocked words" and "Always allowed" listing word, language (or `—`), and a Remove button (POST form with antiforgery, `confirm()`).

- [ ] **Step 4: Run tests**

Run: `dotnet test EduConnect.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Controllers/ModerationWordsController.cs EduConnect.Web/ViewModels/ModerationWordsViewModel.cs EduConnect.Web/Views/ModerationWords/Index.cshtml EduConnect.Web/Services/IAuditService.cs EduConnect.Web/Views/Shared/_SidebarContent.cshtml EduConnect.Tests/ModerationWordsAdminTests.cs
git commit -m "Add admin page for moderation words"
```

---

### Task 5: Private starting list, migration and live check (no commit of the list)

**Files:**
- Modify (gitignored, never committed): `EduConnect.Web/appsettings.Development.json` — add `"Moderation": { "SeedWords": "<comma-separated list>" }`.

- [ ] **Step 1: Confirm the file is ignored**

Run: `git -C .. check-ignore -v src/EduConnect.Web/appsettings.Development.json`
Expected: prints the `.gitignore:13` rule. If it prints nothing, STOP.

- [ ] **Step 2: Write the starting list into that file only**

English profanity and slurs; Tagalog and Taglish profanity with vowel-dropped short forms, joined-without-space forms, common affix forms; multi-word phrases. Leave out ambiguous short forms with common innocent meanings (e.g. `gg`). Comma-separated, one string, so the same value can be pasted into Azure.

- [ ] **Step 3: Apply the migration locally and run the app**

Run: `dotnet ef database update --project EduConnect.Web`, then start the app (port 5120 per the project's run config).
Expected: startup imports the list (table has prohibited rows); no warning logged.

- [ ] **Step 4: Check it in the browser** (student test login): create a group with a listed word in the name → blocked with the exact copy and inputs kept; in a group chat, send a sentence with a listed word → the word shows as stars for both members; as admin, open `/Admin/ModerationWords`, add a word, send it in chat → masked.

- [ ] **Step 5: Leak check before handing over**

Run: `git diff main --stat` and grep the branch diff for a sample of the real words from the list.
Expected: no matches; `appsettings.Development.json` not in the diff.

- [ ] **Step 6: Hand over** — tell the user the Azure step: add App Service setting `Moderation__SeedWords` with the same value as `Moderation:SeedWords` in their `appsettings.Development.json`, before or right after the next deploy.
