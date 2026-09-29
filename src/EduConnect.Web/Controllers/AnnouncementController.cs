using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class AnnouncementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AnnouncementController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IAudienceService _audience;
        private readonly IApprovalService _approval;

        private const string PhotoContainer = "announcements";

        public AnnouncementController(
            ApplicationDbContext context,
            ILogger<AnnouncementController> logger,
            IWebHostEnvironment environment,
            INotificationService notificationService,
            IEmailService emailService,
            IBlobStorageService blobStorageService,
            IAudienceService audience,
            IApprovalService approval)
        {
            _approval = approval;
            _context = context;
            _logger = logger;
            _environment = environment;
            _notificationService = notificationService;
            _emailService = emailService;
            _blobStorageService = blobStorageService;
            _audience = audience;
        }

        // ─── CHECK LOGIN HELPER ────────────────
        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session
                .GetString("UserID"));

        private string GetRoleName() =>
            HttpContext.Session.GetString("RoleName");

        // ─── CHECK IF CAN PUBLISH ──────────────
        private bool CanPublish()
        {
            var role = GetRoleName();
            return role == RoleNames.Dean ||
                   role == RoleNames.Chairperson;
        }

        private bool CanEditAnnouncement(Announcement a) =>
            a.AuthorID == GetUserID();

        private bool IsFaculty() =>
            GetRoleName() == RoleNames.Faculty;

        private bool CanCreate()
        {
            var role = GetRoleName();
            return role == RoleNames.Dean ||
                   role == RoleNames.Chairperson ||
                   role == RoleNames.Faculty;
        }

        // Departments flag their own emergencies. The flag pins the
        // announcement to the top of the tagged departments' feeds and shows
        // a red badge — it never widens who can see it. Campus-wide reach
        // still comes only from the School Wide ("ALL") tag.
        private bool CanSetEmergency()
        {
            var role = GetRoleName();
            return role == RoleNames.Dean ||
                   role == RoleNames.Chairperson ||
                   role == RoleNames.Faculty;
        }

        // Tags a user may target: their own UserDepartments rows. Admins
        // do not author announcements (CanCreate excludes them), so there
        // is no "all tags" case.
        private Task<List<int>> GetUserTagIDsAsync(int userID) =>
            _context.UserDepartments
                .Where(ud => ud.UserID == userID)
                .Select(ud => ud.TagID)
                .ToListAsync();

        // Tags that have become colleges are targeted through the hierarchy
        // now, so they are not offered as tags.
        private async Task<List<DepartmentTag>> GetSelectableTagsAsync(int userID)
        {
            var tagIDs = await GetUserTagIDsAsync(userID);
            return await _context.DepartmentTags
                .Include(d => d.TagType)
                .Where(d => d.IsActive && tagIDs.Contains(d.TagID) &&
                            !_context.Colleges.Any(c => c.LegacyTagID == d.TagID))
                .OrderBy(d => d.TagName)
                .ToListAsync();
        }

        private async Task PopulateAudienceAsync(AnnouncementFormViewModel model, int userID)
        {
            model.AvailableTags = await GetSelectableTagsAsync(userID);
            model.TargetOptions = await _audience.GetTargetOptionsAsync(userID);

            // Editing: show targets the author cannot pick but the
            // announcement already has, so they are kept unless unticked.
            if (model.AnnouncementID != 0)
                model.KeptTargets = await _audience.GetOutOfScopeTargetsAsync(userID, model.AnnouncementID);
        }

        private static TargetSelection SelectionOf(AnnouncementFormViewModel model) =>
            new(model.TargetCollegeIDs ?? new(), model.TargetDepartmentIDs ?? new(), model.TargetProgramIDs ?? new());

        private void SaveTargets(Announcement announcement, AnnouncementFormViewModel model)
        {
            foreach (var id in (model.TargetCollegeIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, CollegeID = id });
            foreach (var id in (model.TargetDepartmentIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, DepartmentID = id });
            foreach (var id in (model.TargetProgramIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, ProgramID = id });
        }

        // Tags and targets decide reach. IsEmergency never widens it — it
        // only pins and badges the announcement for whoever they already
        // reach. School Wide ("ALL") is the campus-wide lever.
        private async Task NotifyReviewersAsync(Announcement announcement, IEnumerable<User> reviewers,
            string message, string subject, string intro)
        {
            foreach (var reviewer in reviewers)
            {
                await _notificationService.SendAsync(
                    reviewer.UserID,
                    "AnnouncementReview",
                    message,
                    $"/Announcement/Review/{announcement.AnnouncementID}",
                    announcement.AnnouncementID);

                _ = _emailService.SendEmailAsync(
                    reviewer.Email,
                    $"{reviewer.FirstName} {reviewer.LastName}",
                    subject,
                    $"<p>Hello {reviewer.FirstName},</p>" +
                    $"<p>{intro}: <strong>{announcement.Title}</strong></p>" +
                    $"<p><a href='https://localhost:7135/Announcement/Review/" +
                    $"{announcement.AnnouncementID}'>Click here to review</a></p>");
            }
        }

        private async Task NotifyAuthorAsync(Announcement announcement, string type,
            string message, string subject, string bodyHtml)
        {
            await _notificationService.SendAsync(
                announcement.AuthorID, type, message,
                "/Announcement/MyAnnouncements", announcement.AnnouncementID);

            var author = await _context.Users.FindAsync(announcement.AuthorID);
            if (author != null)
                _ = _emailService.SendEmailAsync(
                    author.Email,
                    $"{author.FirstName} {author.LastName}",
                    subject,
                    $"<p>Hello {author.FirstName},</p>{bodyHtml}");
        }

        private async Task NotifyAsync(Announcement announcement, int authorId)
        {
            var recipientIds = await _audience.GetRecipientIdsAsync(announcement.AnnouncementID, authorId);
            if (recipientIds.Count > 0)
                await _notificationService.SendToManyAsync(
                    recipientIds,
                    "Announcement",
                    $"New announcement: {announcement.Title}",
                    $"/Announcement/Details/{announcement.AnnouncementID}",
                    announcement.AnnouncementID);
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement
        //  List all announcements
        // ═══════════════════════════════════════
        public async Task<IActionResult> Index(
            string? searchQuery,
            string? feedType,
            int? categoryID,
            int page = 1)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();
            var roleName = GetRoleName();
            bool canPublish = CanPublish();
            int pageSize = 10;

            var query = _context.Announcements
                .Include(a => a.Category)
                .Include(a => a.Author)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .Where(a =>
                    (a.Status == "Published" &&
                     (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now)) ||
                    (canPublish && a.AuthorID == userID))
                .AsQueryable();

            // Feed type filter
            if (!string.IsNullOrEmpty(feedType))
                query = query.Where(a =>
                    a.FeedType == feedType);

            // Category filter
            if (categoryID.HasValue)
                query = query.Where(a =>
                    a.CategoryID == categoryID);

            // Search filter
            if (!string.IsNullOrEmpty(searchQuery))
                query = query.Where(a =>
                    a.Title.Contains(searchQuery) ||
                    a.Body.Contains(searchQuery));

            // Fail closed: only the Administrator reads every announcement;
            // everyone else — Deans and Chairpersons included — sees their
            // own scope. Naming the scoped roles instead would silently
            // expose any role missing from the list.
            if (roleName != RoleNames.Administrator)
            {
                var viewer = await _audience.GetViewerAsync(userID);
                query = query.Where(_audience.VisibleTo(viewer));
            }

            // Total count for pagination
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(
                totalItems / (double)pageSize);

            // Get announcements for current page
            var announcements = await query
                .OrderByDescending(a => a.IsEmergency)
                .ThenByDescending(a => a.Priority)
                .ThenByDescending(a => a.PublishedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AnnouncementTableViewModel
                {
                    AnnouncementID = a.AnnouncementID,
                    AuthorID = a.AuthorID,
                    Title = a.Title,
                    CategoryName = a.Category.CategoryName,
                    CategoryColor = a.Category.ColorHex,
                    FeedType = a.FeedType,
                    IsEmergency = a.IsEmergency,
                    AuthorName = a.Author.FirstName
                                   + " " + a.Author.LastName,
                    Status = a.Status,
                    ViewCount = a.ViewCount,
                    PublishedAt = a.PublishedAt,
                    Tags = a.AnnouncementTags
                        .Select(at =>
                            at.DepartmentTag.ShortName)
                        .ToList()
                })
                .ToListAsync();
            await _audience.AddTargetLabelsAsync(announcements);

            // Pass data to view
            ViewBag.Announcements = announcements;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.FeedType = feedType;
            ViewBag.CategoryID = categoryID;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.Categories = await _context
                .AnnouncementCategories
                .Where(c => c.IsActive)
                .ToListAsync();
            ViewBag.CanPublish = canPublish;
            ViewBag.CurrentUserID = userID;

            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/Details/5
        //  View announcement details
        // ═══════════════════════════════════════
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();

            // Intentionally NOT department-scoped, unlike Index(). Department
            // tags rank relevance, not access — the Explore section of
            // FeedRankingService deliberately links students to other
            // departments' announcements and depends on this staying open.
            var announcement = await _context.Announcements
                .Include(a => a.Category)
                .Include(a => a.Author)
                    .ThenInclude(u => u.Role)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .Include(a => a.Feedbacks)
                    .ThenInclude(f => f.User)
                .FirstOrDefaultAsync(a =>
                        a.AnnouncementID == id &&
                        a.Status == "Published" &&
                        (a.ExpiresAt == null ||
                         a.ExpiresAt > DateTime.Now));

            if (announcement == null)
                return NotFound();

            // Increment view count and record interaction for feed ranking
            announcement.ViewCount++;
            _context.UserAnnouncementInteractions.Add(
                new UserAnnouncementInteraction
                {
                    UserID = userID,
                    AnnouncementID = id,
                    ViewedAt = DateTime.Now
                });
            await _context.SaveChangesAsync();

            // Build detail view model
            var model = new AnnouncementDetailViewModel
            {
                AnnouncementID = announcement.AnnouncementID,
                Title = announcement.Title,
                Body = announcement.Body,
                AISummary = announcement.AISummary,
                FeedType = announcement.FeedType,
                Status = announcement.Status,
                Priority = announcement.Priority,
                IsEmergency = announcement.IsEmergency,
                ViewCount = announcement.ViewCount,
                AttachmentURL = announcement.AttachmentURL,
                PublishedAt = announcement.PublishedAt,
                ExpiresAt = announcement.ExpiresAt,
                CreatedAt = announcement.CreatedAt,
                AuthorName = announcement.Author.FirstName
                               + " " + announcement.Author.LastName,
                AuthorRole = announcement.Author.Role.RoleName,
                CategoryName = announcement.Category.CategoryName,
                CategoryColor = announcement.Category.ColorHex,
                Tags = announcement.AnnouncementTags
                    .Select(at => at.DepartmentTag.TagName)
                    .ToList(),
                AverageRating = announcement.Feedbacks.Any()
                    ? announcement.Feedbacks
                        .Where(f => f.Rating.HasValue)
                        .Average(f => (double)f.Rating.Value)
                    : 0,
                TotalFeedback = announcement.Feedbacks.Count,
                UserHasRated = announcement.Feedbacks
                    .Any(f => f.UserID == userID),
                Feedbacks = announcement.Feedbacks
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(10)
                    .Select(f => new FeedbackItemViewModel
                    {
                        FeedbackID = f.FeedbackID,
                        UserName = f.User.FirstName
                                       + " " + f.User.LastName,
                        FeedbackText = f.FeedbackText,
                        Rating = f.Rating,
                        SentimentLabel = f.SentimentLabel,
                        CreatedAt = f.CreatedAt
                    })
                    .ToList()
            };

            // Tags that are now colleges are shown once, by their college.
            model.Tags = await _audience.GetAudienceNamesAsync(announcement.AnnouncementID);

            return View(model);
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/Create
        //  Show create form
        // ═══════════════════════════════════════
        public async Task<IActionResult> Create()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!CanCreate())
                return RedirectToAction("Index");

            var userID = GetUserID();

            var model = new AnnouncementFormViewModel
            {
                CanSetEmergency = CanSetEmergency(),
                Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync()
            };

            await PopulateAudienceAsync(model, userID);

            ViewBag.IsFaculty = IsFaculty();
            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Create
        //  Save new announcement
        // ═══════════════════════════════════════
        // ─── POST: /Announcement/Create ────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
        public async Task<IActionResult> Create(
            AnnouncementFormViewModel model,
            string? action = null)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!CanCreate())
                return RedirectToAction("Index");

            var userID = GetUserID();

            // The form only renders this toggle for roles that may set it,
            // but a disabled/absent input proves nothing about what was
            // posted — re-assert it here. Assigning the flag on the model
            // also covers every validation re-render below.
            model.CanSetEmergency = CanSetEmergency();
            if (!model.CanSetEmergency)
                model.IsEmergency = false;

            // ─── SECURITY: Validate tags ───────────
            // Make sure faculty didn't tamper with
            // the form to post to other departments
            if (model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                var allowedTagIDs = (await GetSelectableTagsAsync(userID)).Select(t => t.TagID).ToList();

                // Check if any selected tag is NOT allowed
                var unauthorizedTags = model.SelectedTagIDs
                    .Where(id => !allowedTagIDs.Contains(id))
                    .ToList();

                if (unauthorizedTags.Any())
                {
                    ModelState.AddModelError("SelectedTagIDs",
                        "You can only post to your " +
                        "own department.");
                    model.Categories = await _context
                        .AnnouncementCategories
                        .Where(c => c.IsActive)
                        .ToListAsync();
                    await PopulateAudienceAsync(model, userID);
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }
            }

            var targetCheck = await _audience.ValidateTargetsAsync(userID, SelectionOf(model));
            if (!targetCheck.Ok)
                ModelState.AddModelError("SelectedTagIDs", targetCheck.Error!);

            if ((model.SelectedTagIDs == null || !model.SelectedTagIDs.Any()) &&
                SelectionOf(model).IsEmpty)
                ModelState.AddModelError("SelectedTagIDs",
                    "Choose at least one audience: a program, department, college or tag.");

            // A Chairperson can hand a Dean-level post to the college's Deans
            // for final approval instead of publishing it.
            ApprovalRouting? deanRouting = null;
            if (GetRoleName() == RoleNames.Chairperson && model.RequiresDeanApproval)
            {
                deanRouting = await _approval.RouteAsync(userID, deanOnly: true);
                if (!deanRouting.Ok)
                    ModelState.AddModelError("RequiresDeanApproval", deanRouting.Error!);
            }

            if (!ModelState.IsValid)
            {
                // Reload dropdowns
                model.Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync();

                await PopulateAudienceAsync(model, userID);

                ViewBag.IsFaculty = IsFaculty();
                return View(model);
            }

            // Faculty always drafts for review. Dean / Chairperson need no
            // review, so they pick: publish straight away, or park it as a
            // draft that's already "Approved" and simply not out yet — unless
            // a Chairperson sends it to the Dean.
            string approvalStatus;
            string status;
            DateTime? publishedAt;

            if (IsFaculty())
            {
                approvalStatus = "Draft";
                status = "Draft";
                publishedAt = null;
            }
            else if (deanRouting != null && deanRouting.Ok)
            {
                approvalStatus = "PendingDean";
                status = "Draft";
                publishedAt = null;
            }
            else if (action == "draft")
            {
                approvalStatus = "Approved";
                status = "Draft";
                publishedAt = null;
            }
            else
            {
                approvalStatus = "Approved";
                status = "Published";
                publishedAt = DateTime.Now;
            }

            // Handle photo upload
            string? photoURL = null;
            if (model.Photo != null &&
                model.Photo.Length > 0)
            {
                var allowedTypes = new[]
                {
            ".jpg", ".jpeg", ".png",
            ".gif", ".webp"
        };

                var extension = Path.GetExtension(
                    model.Photo.FileName ?? string.Empty).ToLowerInvariant();

                if (!allowedTypes.Contains(extension))
                {
                    ModelState.AddModelError("Photo",
                        "Only image files are allowed.");
                    model.Categories = await _context.AnnouncementCategories
                        .Where(c => c.IsActive).ToListAsync();
                    await PopulateAudienceAsync(model, userID);
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }

                if (model.Photo.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Photo",
                        "File size cannot exceed 5MB.");
                    model.Categories = await _context.AnnouncementCategories
                        .Where(c => c.IsActive).ToListAsync();
                    await PopulateAudienceAsync(model, userID);
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }

                try
                {
                    byte[] photoBytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        await model.Photo.CopyToAsync(memoryStream);
                        photoBytes = memoryStream.ToArray();
                    }

                    var fileName = Guid.NewGuid().ToString() + extension;

                    photoURL = await _blobStorageService.UploadAsync(
                        photoBytes, fileName, PhotoContainer, model.Photo.ContentType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Photo upload failed: {Error}",
                        ex.Message);
                }
            }

            // Derive FeedType from the selected category
            var category = await _context.AnnouncementCategories
                .FindAsync(model.CategoryID);

            // Create announcement
            var announcement = new Announcement
            {
                AuthorID = userID,
                CategoryID = model.CategoryID,
                FeedType = category?.FeedType ?? "NonAcademic",
                Title = model.Title,
                Body = model.Body,
                Priority = model.Priority,
                IsEmergency = model.IsEmergency,
                AttachmentURL = photoURL,
                ExpiresAt = model.ExpiresAt,
                Status = status,
                ApprovalStatus = approvalStatus,
                PublishedAt = publishedAt,
                SubmittedAt = deanRouting != null && deanRouting.Ok ? DateTime.Now : null,
                CreatedAt = DateTime.Now
            };

            try
            {
                _context.Announcements.Add(announcement);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to save announcement: {Error}", ex.Message);
                ModelState.AddModelError("", "Unable to save the announcement. Please try again.");
                model.Categories = await _context.AnnouncementCategories.Where(c => c.IsActive).ToListAsync();
                await PopulateAudienceAsync(model, userID);
                ViewBag.IsFaculty = IsFaculty();
                return View(model);
            }

            // Save tags and targets
            foreach (var tagID in model.SelectedTagIDs ?? new List<int>())
            {
                _context.AnnouncementTags.Add(
                    new AnnouncementTag
                    {
                        AnnouncementID = announcement.AnnouncementID,
                        TagID = tagID,
                        CreatedAt = DateTime.Now
                    });
            }
            SaveTargets(announcement, model);
            await _context.SaveChangesAsync();

            if (announcement.Status == "Published")
                await NotifyAsync(announcement, userID);

            if (announcement.ApprovalStatus == "PendingDean")
            {
                await NotifyReviewersAsync(announcement, deanRouting!.Reviewers,
                    $"Announcement pending your approval: {announcement.Title}",
                    "EduConnect: Announcement Pending Your Approval",
                    "A Chairperson has sent you an announcement for final approval");
                TempData["Success"] = "Sent to the Dean for final approval.";
                return RedirectToAction("MyAnnouncements");
            }

            if (announcement.Status != "Published")
            {
                TempData["Success"] = IsFaculty()
                    ? "Draft saved. Submit it for review when ready."
                    : "Draft saved. Publish it when ready.";
                return RedirectToAction("MyAnnouncements");
            }

            TempData["Success"] =
                "Announcement published successfully!";
            return RedirectToAction("Index");
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/SubmitFeedback
        //  Submit feedback on announcement
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFeedback(
            FeedbackFormViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();

            // Check if already rated
            var existing = await _context.Feedbacks
                .FirstOrDefaultAsync(f =>
                    f.AnnouncementID == model.AnnouncementID &&
                    f.UserID == userID);

            if (existing != null)
            {
                TempData["Error"] =
                    "You have already submitted feedback.";
                return RedirectToAction("Details",
                    new { id = model.AnnouncementID });
            }

            var feedback = new Feedback
            {
                AnnouncementID = model.AnnouncementID,
                UserID = userID,
                Rating = model.Rating,
                FeedbackText = model.FeedbackText,
                IsAcknowledged = false,
                CreatedAt = DateTime.Now
            };

            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Feedback submitted successfully!";
            return RedirectToAction("Details",
                new { id = model.AnnouncementID });
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/Edit/5
        // ═══════════════════════════════════════
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .Include(a => a.AnnouncementTargets)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id);

            if (announcement == null)
                return NotFound();

            if (!CanEditAnnouncement(announcement))
                return RedirectToAction("Index");

            // Administrators may have authored announcements before they
            // lost the ability to announce; they can still archive those,
            // but editing needs a tag picker only authoring roles have, so
            // the views offer no Edit link and a typed URL lands on Details.
            if (!CanCreate())
                return RedirectToAction("Details", new { id });

            if (IsFaculty() &&
                announcement.ApprovalStatus != "Draft" &&
                announcement.ApprovalStatus != "Rejected")
            {
                TempData["Error"] =
                    "This announcement cannot be edited while under review.";
                return RedirectToAction("MyAnnouncements");
            }

            var model = new AnnouncementFormViewModel
            {
                AnnouncementID = announcement.AnnouncementID,
                Title = announcement.Title,
                Body = announcement.Body,
                CategoryID = announcement.CategoryID,
                Priority = announcement.Priority,
                IsEmergency = announcement.IsEmergency,
                CanSetEmergency = CanSetEmergency(),
                ExpiresAt = announcement.ExpiresAt,
                ExistingPhotoURL = announcement.AttachmentURL,
                SelectedTagIDs = announcement.AnnouncementTags
                    .Select(at => at.TagID)
                    .ToList(),
                TargetCollegeIDs = announcement.AnnouncementTargets.Where(t => t.CollegeID != null).Select(t => t.CollegeID!.Value).ToList(),
                TargetDepartmentIDs = announcement.AnnouncementTargets.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID!.Value).ToList(),
                TargetProgramIDs = announcement.AnnouncementTargets.Where(t => t.ProgramID != null).Select(t => t.ProgramID!.Value).ToList(),
                Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync()
            };

            await PopulateAudienceAsync(model, userID);

            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Edit/5
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
        public async Task<IActionResult> Edit(
            AnnouncementFormViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();
            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .Include(a => a.AnnouncementTargets)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == model.AnnouncementID);

            if (announcement == null)
                return NotFound();

            // Re-check ownership server-side
            if (!CanEditAnnouncement(announcement))
                return RedirectToAction("Index");

            if (!CanCreate())
                return RedirectToAction("Details",
                    new { id = model.AnnouncementID });

            if (IsFaculty() &&
                announcement.ApprovalStatus != "Draft" &&
                announcement.ApprovalStatus != "Rejected")
            {
                TempData["Error"] =
                    "This announcement cannot be edited while under review.";
                return RedirectToAction("MyAnnouncements");
            }

            // The form only renders this toggle for roles that may set it,
            // but a disabled/absent input proves nothing about what was
            // posted — re-assert it here. Assigning the flag on the model
            // also covers every validation re-render below.
            model.CanSetEmergency = CanSetEmergency();
            if (!model.CanSetEmergency)
                model.IsEmergency = false;

            // Tag security: authors may only target their own tags
            if (model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                var allowedTagIDs = (await GetSelectableTagsAsync(userID)).Select(t => t.TagID).ToList();

                var unauthorized = model.SelectedTagIDs
                    .Where(id => !allowedTagIDs.Contains(id))
                    .ToList();

                if (unauthorized.Any())
                    ModelState.AddModelError("SelectedTagIDs",
                        "You can only post to your " +
                        "own department.");
            }

            // Targets the announcement already has may stay even when the
            // author could not pick them now (e.g. a college target on a
            // Chairperson's older post); editing must not silently narrow it.
            var existing = new TargetSelection(
                announcement.AnnouncementTargets.Where(t => t.CollegeID != null).Select(t => t.CollegeID!.Value).ToList(),
                announcement.AnnouncementTargets.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID!.Value).ToList(),
                announcement.AnnouncementTargets.Where(t => t.ProgramID != null).Select(t => t.ProgramID!.Value).ToList());
            var targetCheck = await _audience.ValidateTargetsAsync(userID, SelectionOf(model), keep: existing);
            if (!targetCheck.Ok)
                ModelState.AddModelError("SelectedTagIDs", targetCheck.Error!);

            if ((model.SelectedTagIDs == null || !model.SelectedTagIDs.Any()) &&
                SelectionOf(model).IsEmpty)
                ModelState.AddModelError("SelectedTagIDs",
                    "Choose at least one audience: a program, department, college or tag.");

            if (!ModelState.IsValid)
            {
                model.ExistingPhotoURL =
                    announcement.AttachmentURL;
                model.Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync();

                await PopulateAudienceAsync(model, userID);

                return View(model);
            }

            // ─── Photo handling ────────────────
            if (model.RemovePhoto)
            {
                await DeletePhotoBlobAsync(announcement.AttachmentURL);
                announcement.AttachmentURL = null;
            }
            else if (model.Photo != null &&
                     model.Photo.Length > 0)
            {
                var allowedTypes = new[]
                {
                    ".jpg", ".jpeg", ".png",
                    ".gif", ".webp"
                };
                var ext = Path.GetExtension(
                    model.Photo.FileName ?? string.Empty)
                    .ToLowerInvariant();

                if (!allowedTypes.Contains(ext))
                {
                    ModelState.AddModelError("Photo",
                        "Only image files are allowed.");
                    model.ExistingPhotoURL =
                        announcement.AttachmentURL;
                    model.Categories = await _context
                        .AnnouncementCategories
                        .Where(c => c.IsActive)
                        .ToListAsync();
                    await PopulateAudienceAsync(model, userID);
                    return View(model);
                }

                if (model.Photo.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Photo",
                        "File size cannot exceed 5MB.");
                    model.ExistingPhotoURL =
                        announcement.AttachmentURL;
                    model.Categories = await _context
                        .AnnouncementCategories
                        .Where(c => c.IsActive)
                        .ToListAsync();
                    await PopulateAudienceAsync(model, userID);
                    return View(model);
                }

                string? newPhotoURL = null;
                try
                {
                    byte[] photoBytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        await model.Photo.CopyToAsync(memoryStream);
                        photoBytes = memoryStream.ToArray();
                    }

                    var fileName = Guid.NewGuid().ToString() + ext;

                    newPhotoURL = await _blobStorageService.UploadAsync(
                        photoBytes, fileName, PhotoContainer, model.Photo.ContentType);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Photo upload failed: {Error}", ex.Message);
                }

                if (newPhotoURL != null)
                {
                    await DeletePhotoBlobAsync(announcement.AttachmentURL);
                    announcement.AttachmentURL = newPhotoURL;
                }
            }

            // Reset to Draft if Faculty edits a rejected announcement
            if (IsFaculty() && announcement.ApprovalStatus == "Rejected")
            {
                announcement.ApprovalStatus = "Draft";
                announcement.ChairRejectionReason = null;
                announcement.RejectionReason = null;
            }

            // Derive FeedType from the selected category
            var category = await _context.AnnouncementCategories
                .FindAsync(model.CategoryID);

            // ─── Update fields ─────────────────
            announcement.Title = model.Title;
            announcement.Body = model.Body;
            announcement.CategoryID = model.CategoryID;
            announcement.FeedType = category?.FeedType ?? "NonAcademic";
            announcement.Priority = model.Priority;
            announcement.IsEmergency = model.IsEmergency;
            announcement.ExpiresAt = model.ExpiresAt;
            announcement.UpdatedAt = DateTime.Now;

            // ─── Replace tags and targets ──────
            _context.AnnouncementTags
                .RemoveRange(announcement.AnnouncementTags);
            _context.AnnouncementTargets
                .RemoveRange(announcement.AnnouncementTargets);
            SaveTargets(announcement, model);

            if (model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                foreach (var tagID in model.SelectedTagIDs)
                {
                    _context.AnnouncementTags.Add(
                        new AnnouncementTag
                        {
                            AnnouncementID =
                                announcement.AnnouncementID,
                            TagID = tagID,
                            CreatedAt = DateTime.Now
                        });
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update announcement: {Error}", ex.Message);
                ModelState.AddModelError("", "Unable to save changes. Please try again.");
                model.ExistingPhotoURL = announcement.AttachmentURL;
                model.Categories = await _context.AnnouncementCategories.Where(c => c.IsActive).ToListAsync();
                await PopulateAudienceAsync(model, userID);
                ViewBag.IsFaculty = IsFaculty();
                return View(model);
            }

            TempData["Success"] =
                "Announcement updated successfully!";

            if (IsFaculty())
                return RedirectToAction("MyAnnouncements");

            return RedirectToAction("Index");
        }

        private async Task DeletePhotoBlobAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.Contains($"/{PhotoContainer}/", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var blobName = url.Substring(url.LastIndexOf('/') + 1);
                await _blobStorageService.DeleteAsync(blobName, PhotoContainer);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to delete photo blob: {Error}", ex.Message);
            }
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Delete/5
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var announcement = await _context.Announcements
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id);

            if (announcement == null)
                return NotFound();

            if (!CanEditAnnouncement(announcement))
                return RedirectToAction("Index");

            announcement.Status = "Archived";
            announcement.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Announcement archived successfully!";
            return RedirectToAction("Index");
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/MyAnnouncements
        //  Faculty's own announcement list
        // ═══════════════════════════════════════
        public async Task<IActionResult> MyAnnouncements()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var role = GetRoleName();
            if (role != RoleNames.Faculty && role != RoleNames.Dean && role != RoleNames.Chairperson)
                return RedirectToAction("Index");

            var userID = GetUserID();

            var announcements = await _context.Announcements
                .Include(a => a.Category)
                .Where(a => a.AuthorID == userID)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.AnnouncementID,
                    a.Title,
                    a.FeedType,
                    a.Status,
                    a.ApprovalStatus,
                    a.SubmittedAt,
                    a.CreatedAt,
                    a.ChairRejectionReason,
                    a.RejectionReason,
                    CategoryName = a.Category.CategoryName
                })
                .ToListAsync();

            ViewBag.Announcements = announcements;
            return View();
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Submit/{id}
        //  Faculty submits a draft for review
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!IsFaculty())
                return RedirectToAction("Index");

            var userID = GetUserID();

            var announcement = await _context.Announcements
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.AuthorID == userID &&
                    (a.ApprovalStatus == "Draft" ||
                     a.ApprovalStatus == "Rejected"));

            if (announcement == null)
                return RedirectToAction("MyAnnouncements");

            // Programs, departments and colleges count as much as tags.
            if (!await _audience.HasAudienceAsync(announcement.AnnouncementID))
            {
                TempData["Error"] = "Please choose an audience before submitting.";
                return RedirectToAction("MyAnnouncements");
            }

            // The department's Chairpersons, or the college's Deans when the
            // department has none.
            var routing = await _approval.RouteAsync(userID, deanOnly: false);
            if (!routing.Ok)
            {
                TempData["Error"] = routing.Error;
                return RedirectToAction("MyAnnouncements");
            }

            // Clear stale data from any prior rejected cycle
            announcement.ChairApprovedByID = null;
            announcement.ChairApprovedAt = null;
            announcement.ChairRejectionReason = null;
            announcement.ApprovedByID = null;
            announcement.ApprovedAt = null;
            announcement.RejectionReason = null;

            announcement.ApprovalStatus = routing.Status;
            announcement.SubmittedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            await NotifyReviewersAsync(announcement, routing.Reviewers,
                $"New announcement pending your review: {announcement.Title}",
                "EduConnect: Announcement Pending Review",
                "A new announcement requires your review");

            TempData["Success"] = "Announcement submitted for review.";
            return RedirectToAction("MyAnnouncements");
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/ReviewQueue
        //  Chairperson / Dean pending review list
        // ═══════════════════════════════════════
        public async Task<IActionResult> ReviewQueue()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcements = await _approval.ReviewableBy(viewer)
                .OrderBy(a => a.SubmittedAt)
                .Select(a => new
                {
                    a.AnnouncementID,
                    a.Title,
                    a.FeedType,
                    a.SubmittedAt,
                    AuthorName = a.Author.FirstName + " " + a.Author.LastName,
                    CategoryName = a.Category.CategoryName
                })
                .ToListAsync();

            ViewBag.Announcements = announcements;
            ViewBag.Role = roleName;
            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/Review/{id}
        //  Full preview for reviewer
        // ═══════════════════════════════════════
        public async Task<IActionResult> Review(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcement = await _approval.ReviewableBy(viewer)
                .Include(a => a.Author)
                    .ThenInclude(u => u.Role)
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            ViewBag.Role = roleName;
            ViewBag.AudienceNames = await _audience.GetAudienceNamesAsync(id);

            // Escalation needs an active Dean in the author's college.
            if (roleName == RoleNames.Chairperson)
                ViewBag.CanEscalate =
                    (await _approval.RouteAsync(announcement.AuthorID, deanOnly: true)).Ok;

            return View(announcement);
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Approve/{id}
        //  Chairperson or Dean approves
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, bool escalateToDean = false)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var userID = GetUserID();
            var viewer = await _audience.GetViewerAsync(userID);

            var announcement = await _approval.ReviewableBy(viewer)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            if (roleName == RoleNames.Chairperson)
            {
                announcement.ChairApprovedByID = userID;
                announcement.ChairApprovedAt = DateTime.Now;

                // A Dean-level matter goes on to the Dean; otherwise the
                // Chairperson's approval is final.
                if (escalateToDean)
                {
                    var routing = await _approval.RouteAsync(announcement.AuthorID, deanOnly: true);
                    if (!routing.Ok)
                    {
                        TempData["Error"] = routing.Error;
                        return RedirectToAction("Review", new { id });
                    }

                    announcement.ApprovalStatus = "PendingDean";
                    await _context.SaveChangesAsync();

                    await NotifyReviewersAsync(announcement, routing.Reviewers,
                        $"Announcement forwarded for your review: {announcement.Title}",
                        "EduConnect: Announcement Pending Your Approval",
                        "An announcement approved by the Chairperson now needs your final approval");

                    TempData["Success"] = "Approved and sent to the Dean for final approval.";
                    return RedirectToAction("ReviewQueue");
                }
            }

            announcement.ApprovalStatus = "Approved";
            announcement.ApprovedByID = userID;
            announcement.ApprovedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            var approvedBy = roleName == RoleNames.Chairperson ? "the Chairperson" : "the Dean";
            await NotifyAuthorAsync(announcement,
                "AnnouncementApproved",
                "Your announcement has been approved — you can now publish it",
                "EduConnect: Announcement Approved",
                $"<p>Your announcement <strong>{announcement.Title}</strong> has been approved by {approvedBy}. " +
                "You can now publish it.</p>" +
                "<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>Go to My Announcements</a></p>");

            TempData["Success"] = "Announcement approved. The author has been notified.";
            return RedirectToAction("ReviewQueue");
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Reject/{id}
        //  Chairperson or Dean rejects
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string rejectionReason)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["Error"] = "A rejection reason is required.";
                return RedirectToAction("Review", new { id });
            }

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcement = await _approval.ReviewableBy(viewer)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            announcement.ApprovalStatus = "Rejected";
            if (roleName == RoleNames.Chairperson)
                announcement.ChairRejectionReason = rejectionReason;
            else
                announcement.RejectionReason = rejectionReason;
            await _context.SaveChangesAsync();

            var rejectedBy = roleName == RoleNames.Chairperson ? "the Chairperson" : "the Dean";
            await NotifyAuthorAsync(announcement,
                "AnnouncementRejected",
                $"Your announcement was rejected by {rejectedBy}",
                "EduConnect: Announcement Rejected",
                $"<p>Your announcement <strong>{announcement.Title}</strong> was rejected by {rejectedBy}.</p>" +
                $"<p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(rejectionReason)}</p>" +
                "<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
                "Go to My Announcements to revise and resubmit</a></p>");

            TempData["Success"] = "Announcement rejected. The author has been notified.";
            return RedirectToAction("ReviewQueue");
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Publish/{id}
        //  Author publishes an approved announcement
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Faculty &&
                roleName != RoleNames.Dean &&
                roleName != RoleNames.Chairperson)
                return RedirectToAction("Index");

            var userID = GetUserID();

            var announcement = await _context.Announcements
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.AuthorID == userID &&
                    a.ApprovalStatus == "Approved" &&
                    a.Status != "Published");

            if (announcement == null)
                return RedirectToAction("MyAnnouncements");

            announcement.Status = "Published";
            announcement.PublishedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            await NotifyAsync(announcement, userID);

            TempData["Success"] =
                "Announcement published successfully!";
            return RedirectToAction("MyAnnouncements");
        }
    }
}