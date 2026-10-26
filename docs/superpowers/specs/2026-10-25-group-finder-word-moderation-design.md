# Group Finder word moderation — design

Date: 2026-10-25
Status: Draft, awaiting approval

## Why

Group Finder lets students type freely in three places, and none of them is
checked for inappropriate language today:

- group **name** and **description** (`GroupController.Create`, saved after a trim)
- group **chat messages** (`GroupChatHub.SendMessage`, checked only for empty / over 2000 chars)

EduConnect is Adamson University's campus platform, so these spaces should stay
appropriate for a university. Students write in English, Tagalog and Taglish, so
English-only filtering would miss most real cursing.

**Priority (user's):** cursing inside group chats is the most important thing to
stop. Inappropriate group names must still be blocked, because every student sees
them in the Group Finder list.

## Goals

1. Group chat messages have inappropriate words **masked with `*`** before they are
   saved or broadcast. The message still sends. (User's decision: mask, not block.)
2. Creating a group with an inappropriate word in the **name or description** is
   **blocked**. (User's decision.)
3. Detection covers **English, Tagalog and Taglish**, including common evasions.
4. Admins can **add and remove words** without a redeploy. (User approved, from the
   first proposal.)

## Non-goals

- **No offender logging or tracking.** The user dropped this explicitly: the purpose
  is to prevent bad language, not to record who tried. No counts, no strikes, no
  audit rows per offence, no admin report of offenders, no link to
  `ReportingSanction`.
- No moderation outside Group Finder (announcements, org posts, events, chatbot,
  incident reports) in this change.
- No rewriting of messages already in the database. Chats expire 7 days after the
  group is created (`ChatLifetime`), so old messages age out on their own.
- No AI or external moderation service. Plain word-list matching.
- No moderation of usernames / profile fields.

## Anti-goals (would be a failure even if it "works")

- **False positives on innocent words.** "class", "assignment", "Scunthorpe",
  "pukyutan" (bee), "gagamba" (spider), "tangkad", "pakialam", "titser" and similar
  must pass untouched. A filter that mangles ordinary academic chat is worse than none.
- The original bad word being stored anywhere (DB, logs) after masking.
- Any explicit bad word committed to the public GitHub repo (code, migrations,
  tests, docs, commit messages).
- Moderation that only runs in the browser and can be bypassed.
- Telling the student *which* word was blocked in a way that reprints it on screen
  is acceptable; echoing the full unmasked text back into the chat is not.

## Behaviour

### Chat messages (`GroupChatHub.SendMessage`)

- After the existing empty / length / membership checks and before creating the
  `GroupMessage`, run the content through the moderation service's **mask**.
- Each matched bad word is replaced by `*` characters **of the same length as the
  matched text** (so with a listed word `zorbak`, `zorbak mo` → `****** mo`; `z.o.r.b.a.k` → `***********`).
  Everything else in the message stays exactly as the student wrote it, including
  spacing and casing.
- The masked text is what is saved and what is broadcast with `ReceiveMessage`.
  Everyone, the sender included, sees only the masked version.
- No warning popup or toast to the sender; the stars speak for themselves.
  *(Claude's call.)*
- A message that is entirely bad words still sends as stars. *(Claude's call —
  consistent with "mask, don't block".)*

### Group name and description (`GroupController.Create`)

- After the existing required-field / category checks, check `name` and
  `description` with the moderation service's **contains** check.
- If either hits: don't create the group; return the Create view with the error
  "Please remove inappropriate language from the group name or description." and
  **keep what the student typed** (name, category, description, max members) in the
  form. Today the Create POST returns `View()` with no model, so the form loses its
  values on any error; this change should repopulate it (via a small model or
  ViewBag — builder's choice) for the moderation error at least, and may do it for
  the existing errors too.
- There is no Edit action for groups today, so Create is the only entry point.

## Detection rules

One shared service (e.g. `IContentModerationService` in `Services/`, registered in
DI like the other services) with two operations:

- `bool ContainsProhibited(string text)`
- `string Mask(string text)`

Both use the same matcher. Matching rules:

1. **Case-insensitive.**
2. **Whole-word matching.** A list word matches only when it is a whole token, not
   when it sits inside a longer innocent word (a short listed word must not hit
   "class" or "assignment").
3. **Character substitutions** normalised before comparing: `@`→a, `4`→a, `3`→e,
   `1`/`!`/`|`→i, `0`→o, `$`/`5`→s, `7`→t, and `*` treated as a wildcard letter
   inside a word (`z*rbak`, `zorb@k`).
4. **Stretched letters** collapsed: `zorbaaak`, `zooorbak` match their base word.
5. **Separator evasion:** single letters split by spaces, dots, dashes or
   underscores (`z.o.r.b.a.k`, `z o r b a k`, `z-o-r-b-a-k`) are joined and checked.
   Masking covers the whole matched span including the separators.
6. **Multi-word phrases** are supported in the list (e.g. a listed phrase
   `flim bo mo`) and match across normal spacing.
7. **Taglish affixes:** common Tagalog affix / suffix forms of a root count as the
   word (e.g. for a root `zorbak`: `kinazorbak`, `zorbakan`,
   `pinagzorbak`). Simplest acceptable
   implementation: list these forms explicitly in the seed list rather than writing
   an affix engine. *(Claude's call: explicit forms over a morphology engine.)*
8. **Allow-list:** a small list of innocent words that would otherwise trip rule 3
   or 4 is checked first and never masked. Seeded with the anti-goal examples above.

Unicode homoglyphs (Cyrillic look-alikes etc.) are out of scope.

Builder notes from the review *(Claude's calls)*:
- Collapsing stretched letters must not turn a different innocent word into a
  listed one (compare both the raw and the collapsed token, never only the
  collapsed one against a shorter list word).
- Tokens made only of digits (e.g. `455`) are never normalised into letters.
- Words joined without spaces go in the list as explicit forms rather than being
  found by splitting.

## Word list

### Storage

- New table `ModerationWords` (EF entity + migration): `ModerationWordID`, `Word`
  (normalised, unique, max ~100), `Language` (`English` | `Tagalog` | `Taglish`,
  display only), `IsAllowed` (false = prohibited, true = allow-list entry),
  `CreatedAt`.
- **The prohibited words never go in the repo.** The GitHub repo is public, so the
  user decided (2026-10-25) to keep the bad-word list out of source control. No
  explicit word may appear in a migration, seed code, test, doc, commit message or
  committed config file.
- The **migration** creates the table and seeds **only the allow-list** (innocent
  words such as `class`, `pukyutan`, `gagamba`), which is safe to publish.
- The **prohibited starting list** comes from configuration key
  `Moderation:SeedWords`: one string, one word or phrase per line (commas also
  accepted as separators).
  - **Production:** an Azure App Service application setting named
    `Moderation__SeedWords`. The app is deployed by GitHub Actions from the repo,
    so a gitignored file would never reach Azure; the app setting is the private
    channel. The user adds it in the Azure portal before or right after deploy.
  - **Local dev:** the same key in `appsettings.Development.json`, which is already
    gitignored (or user-secrets; the project has a `UserSecretsId`).
- **Import on startup:** when the app starts and `ModerationWords` contains **no
  prohibited words**, it normalises and inserts every word from
  `Moderation:SeedWords`. If the table already has prohibited words, the setting
  is ignored, so words an admin removed are not brought back on the next restart.
  If the setting is missing or empty, the app starts normally, logs one warning
  (with no words in it), and moderation masks nothing until an admin adds words.
  *(Claude's calls: import-once rule, separator format, warning-only on missing.)*
- **Claude writes the starting list** (English profanity and slurs, Tagalog /
  Taglish profanity, vowel-dropped short forms, words joined without spaces,
  and affix forms) and gives it to the user
  outside git: in the gitignored `appsettings.Development.json` for local use,
  and as a value ready to paste into the Azure setting. Ambiguous short forms
  with a common innocent meaning (e.g. `gg`, gaming slang for "good game") are
  left out.
- The service caches the compiled list in memory (e.g. `IMemoryCache`) and clears
  the cache whenever an admin changes the list, so each chat message does not hit
  the DB.

### Admin page (Claude's call on placement)

- New Admin page **Moderation Words** at `/Admin/ModerationWords`, linked in the
  admin sidebar next to Audit Log, guarded by `IsAdmin()` like the rest of
  `AdminController`.
- Shows prohibited words and allow-list entries (two lists or a filter), each with
  a remove button; a small form to add a word with its language and type.
- Adding normalises the word (trim, lowercase) and rejects duplicates and blanks.
- Changes to the list are recorded in the existing audit log (`IAuditService`) as
  an admin action, since that's how other admin configuration changes are tracked.
  This is about who edited the **list**, not about offenders. *(Claude's call.)*
- Because the seed list contains explicit words, the page is admin-only and not
  linked anywhere else.

## Constraints

- ASP.NET Core 8 MVC, EF Core / SQL Server, session-based auth (no `[Authorize]`),
  role names from `RoleNames.cs`. Bootstrap 5.1 and the `ec-*` design system for
  the admin page.
- Moderation must run on the server: in `GroupChatHub` and `GroupController`, never
  only in JavaScript.
- Chat rendering already HTML-escapes content on the client; masking must not
  change that.
- Tests use xUnit with in-memory SQLite (`EduConnect.Tests`, `TestDb.cs`).
- `main` deploys to Azure; the user pushes `main` themselves.
- The GitHub repo is **public**, so no explicit bad words may be committed (see Word list).
- Single app instance (no SignalR backplane), so an in-memory cache cleared on
  admin edits is enough.

## Testing

- **Tests use made-up words only** (e.g. `zorbak`, `flimbo`, a phrase like
  `zor bak mo`), inserted by a test fixture. Real profanity never appears in
  test code. The allow-list examples (`class`, `pukyutan`, ...) may be used as-is.
- Startup import: imports from `Moderation:SeedWords` when there are no prohibited
  words; skips when there are; does nothing (and doesn't crash) when the setting
  is missing.
- Unit tests for the matcher: each detection rule (case, whole word, substitution,
  stretched letters, separators, phrases, affix forms), and the allow-list /
  false-positive examples from Anti-goals.
- `Mask` keeps length and untouched text exactly.
- `GroupController.Create` rejects a bad name and a bad description, keeps the
  typed values, and creates nothing; a clean name still works.
- `GroupChatHub.SendMessage` saves and broadcasts the masked content.
- Admin page: non-admins are refused; add / duplicate / remove; the cache refreshes
  so a newly added word is caught on the next message.

## Left to the builder

- Exact matcher implementation (regex vs token scan), as long as the rules and
  tests above hold.
- Whether the Create form repopulation uses a view model or ViewBag.
- Exact layout of the admin page within the existing admin styling.
- The precise contents of the seed list beyond the examples named here.
