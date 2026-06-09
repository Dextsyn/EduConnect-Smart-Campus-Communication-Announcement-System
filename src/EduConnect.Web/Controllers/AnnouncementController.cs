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

        public AnnouncementController(
            ApplicationDbContext context,
            ILogger<AnnouncementController> logger,
            IWebHostEnvironment environment,
            INotificationService notificationService,
            IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
            _notificationService = notificationService;
            _emailService = emailService;
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
            return role == "Dean" ||
                   role == "Chair Person";
        }

        private bool CanEditAnnouncement(Announcement a) =>
            a.AuthorID == GetUserID();

        private bool IsFaculty() =>
            GetRoleName() == "Faculty";

        private bool CanCreate()
        {
            var role = GetRoleName();
            return role == "Dean" ||
                   role == "Chair Person" ||
                   role == "Faculty";
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

            // Student sees only their dept
            if (roleName == "Student")
            {
                var userTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                query = query.Where(a =>
                    a.AnnouncementTags.Any(at =>
                        userTagIDs.Contains(at.TagID)) ||
                    a.AnnouncementTags.Any(at =>
                        at.DepartmentTag.ShortName == "ALL"));
            }

            // Faculty sees their own dept
            if (roleName == "Faculty" ||
                roleName == "Staff")
            {
                var userTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                query = query.Where(a =>
                    a.AnnouncementTags.Any(at =>
                        userTagIDs.Contains(at.TagID)) ||
                    a.AnnouncementTags.Any(at =>
                        at.DepartmentTag.ShortName == "ALL"));
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

            var roleName = GetRoleName();
            var userID = GetUserID();

            var model = new AnnouncementFormViewModel
            {
                Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync()
            };

            // Faculty sees only Academic tags
            // Staff sees only NonAcademic tags
            // Admin sees all tags
            if (roleName == "Administrator")
            {
                model.AvailableTags = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.TagName)
                    .ToListAsync();
            }
            else
            {
                // Faculty/Dean/Staff sees ONLY
                // their own department tags
                var userTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                model.AvailableTags = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           userTagIDs.Contains(d.TagID))
                    .OrderBy(d => d.TagName)
                    .ToListAsync();

                // Set default FeedType based on role
                if (roleName == "Dean" ||
                    roleName == "Chair Person")
                    model.FeedType = "Academic";
                else
                    model.FeedType = "NonAcademic";
            }

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
        public async Task<IActionResult> Create(
            AnnouncementFormViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!CanCreate())
                return RedirectToAction("Index");

            var roleName = GetRoleName();
            var userID = GetUserID();

            // Strip fields Faculty is not allowed to set
            if (IsFaculty())
            {
                model.IsEmergency = false;
                if (model.FeedType == "Emergency")
                    model.FeedType = "Academic";
            }

            // ─── SECURITY: Validate tags ───────────
            // Make sure faculty didn't tamper with
            // the form to post to other departments
            if (roleName != "Administrator" &&
                model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                // Get faculty's allowed tag IDs
                var allowedTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                // Check if any selected tag is NOT allowed
                var unauthorizedTags = model.SelectedTagIDs
                    .Where(id => !allowedTagIDs.Contains(id))
                    .ToList();

                if (unauthorizedTags.Any())
                {
                    ModelState.AddModelError("",
                        "You can only post to your " +
                        "own department.");
                    model.Categories = await _context
                        .AnnouncementCategories
                        .Where(c => c.IsActive)
                        .ToListAsync();
                    model.AvailableTags = await _context
                        .DepartmentTags
                        .Where(d => d.IsActive &&
                               allowedTagIDs.Contains(d.TagID))
                        .ToListAsync();
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }
            }

            if (!ModelState.IsValid)
            {
                // Reload dropdowns
                model.Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync();

                var allowedIDs = roleName == "Administrator"
                    ? await _context.DepartmentTags
                        .Where(d => d.IsActive)
                        .Select(d => d.TagID)
                        .ToListAsync()
                    : await _context.UserDepartments
                        .Where(ud => ud.UserID == userID)
                        .Select(ud => ud.TagID)
                        .ToListAsync();

                model.AvailableTags = await _context
                    .DepartmentTags
                    .Where(d => d.IsActive &&
                           allowedIDs.Contains(d.TagID))
                    .ToListAsync();

                ViewBag.IsFaculty = IsFaculty();
                return View(model);
            }

            // Faculty saves as Draft; everyone else publishes directly
            string approvalStatus;
            string status;
            DateTime? publishedAt;

            if (IsFaculty())
            {
                approvalStatus = "Draft";
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
                    model.Photo.FileName).ToLowerInvariant();

                if (!allowedTypes.Contains(extension))
                {
                    ModelState.AddModelError("Photo",
                        "Only image files are allowed.");
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }

                if (model.Photo.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Photo",
                        "File size cannot exceed 5MB.");
                    ViewBag.IsFaculty = IsFaculty();
                    return View(model);
                }

                try
                {
                    var uploadsFolder = Path.Combine(
                        _environment.WebRootPath,
                        "uploads", "announcements");

                    Directory.CreateDirectory(uploadsFolder);

                    var fileName = Guid.NewGuid().ToString()
                        + extension;

                    var filePath = Path.Combine(
                        uploadsFolder, fileName);

                    using var stream = new FileStream(
                        filePath, FileMode.Create);
                    await model.Photo.CopyToAsync(stream);

                    photoURL = "/uploads/announcements/"
                             + fileName;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Photo upload failed: {Error}",
                        ex.Message);
                }
            }

            // Create announcement
            var announcement = new Announcement
            {
                AuthorID = userID,
                CategoryID = model.CategoryID,
                FeedType = model.FeedType,
                Title = model.Title,
                Body = model.Body,
                Priority = model.Priority,
                IsEmergency = model.IsEmergency,
                AttachmentURL = photoURL,
                ExpiresAt = model.ExpiresAt,
                Status = status,
                ApprovalStatus = approvalStatus,
                PublishedAt = publishedAt,
                CreatedAt = DateTime.Now
            };

            _context.Announcements.Add(announcement);
            await _context.SaveChangesAsync();

            // Save tags
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
                await _context.SaveChangesAsync();
            }

            // Send real-time notifications to department members
            if (announcement.Status == "Published" &&
                model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                var tags = await _context.DepartmentTags
                    .Where(t => model.SelectedTagIDs.Contains(t.TagID))
                    .ToListAsync();

                bool broadcastAll = tags.Any(t =>
                    t.ShortName == "ALL" || t.ShortName == "Emergency");

                List<int> recipientIds;
                if (broadcastAll)
                {
                    recipientIds = await _context.Users
                        .Where(u => u.IsActive && u.UserID != userID)
                        .Select(u => u.UserID)
                        .ToListAsync();
                }
                else
                {
                    recipientIds = await _context.UserDepartments
                        .Where(ud => model.SelectedTagIDs.Contains(ud.TagID))
                        .Select(ud => ud.UserID)
                        .Distinct()
                        .Where(id => id != userID)
                        .ToListAsync();
                }

                if (recipientIds.Count > 0)
                {
                    await _notificationService.SendToManyAsync(
                        recipientIds,
                        "Announcement",
                        $"New announcement: {announcement.Title}",
                        $"/Announcement/Details/{announcement.AnnouncementID}",
                        announcement.AnnouncementID);
                }
            }

            if (IsFaculty())
            {
                TempData["Success"] =
                    "Draft saved. Submit it for review when ready.";
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
            var roleName = GetRoleName();

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id);

            if (announcement == null)
                return NotFound();

            if (!CanEditAnnouncement(announcement))
                return RedirectToAction("Index");

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
                FeedType = announcement.FeedType,
                Priority = announcement.Priority,
                IsEmergency = announcement.IsEmergency,
                ExpiresAt = announcement.ExpiresAt,
                ExistingPhotoURL = announcement.AttachmentURL,
                SelectedTagIDs = announcement.AnnouncementTags
                    .Select(at => at.TagID)
                    .ToList(),
                Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync()
            };

            if (roleName == "Administrator")
            {
                model.AvailableTags = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.TagName)
                    .ToListAsync();
            }
            else
            {
                var userTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                model.AvailableTags = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           userTagIDs.Contains(d.TagID))
                    .OrderBy(d => d.TagName)
                    .ToListAsync();
            }

            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Edit/5
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            AnnouncementFormViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var userID = GetUserID();
            var roleName = GetRoleName();
            bool isAdmin = roleName == "Administrator";

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == model.AnnouncementID);

            if (announcement == null)
                return NotFound();

            // Re-check ownership server-side
            if (!CanEditAnnouncement(announcement))
                return RedirectToAction("Index");

            if (IsFaculty() &&
                announcement.ApprovalStatus != "Draft" &&
                announcement.ApprovalStatus != "Rejected")
            {
                TempData["Error"] =
                    "This announcement cannot be edited while under review.";
                return RedirectToAction("MyAnnouncements");
            }

            // Strip fields Faculty is not allowed to set
            if (IsFaculty())
            {
                model.IsEmergency = false;
                if (model.FeedType == "Emergency")
                    model.FeedType = "Academic";
            }

            // Tag security for non-admins
            if (!isAdmin &&
                model.SelectedTagIDs != null &&
                model.SelectedTagIDs.Any())
            {
                var allowedTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                var unauthorized = model.SelectedTagIDs
                    .Where(id => !allowedTagIDs.Contains(id))
                    .ToList();

                if (unauthorized.Any())
                    ModelState.AddModelError("",
                        "You can only post to your " +
                        "own department.");
            }

            if (!ModelState.IsValid)
            {
                model.ExistingPhotoURL =
                    announcement.AttachmentURL;
                model.Categories = await _context
                    .AnnouncementCategories
                    .Where(c => c.IsActive)
                    .ToListAsync();

                var allowedIDs = isAdmin
                    ? await _context.DepartmentTags
                        .Where(d => d.IsActive)
                        .Select(d => d.TagID)
                        .ToListAsync()
                    : await _context.UserDepartments
                        .Where(ud => ud.UserID == userID)
                        .Select(ud => ud.TagID)
                        .ToListAsync();

                model.AvailableTags = await _context
                    .DepartmentTags
                    .Include(d => d.TagType)
                    .Where(d => d.IsActive &&
                           allowedIDs.Contains(d.TagID))
                    .OrderBy(d => d.TagName)
                    .ToListAsync();

                return View(model);
            }

            // ─── Photo handling ────────────────
            if (model.RemovePhoto)
            {
                DeletePhotoFile(announcement.AttachmentURL);
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
                    model.Photo.FileName)
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
                    return View(model);
                }

                DeletePhotoFile(announcement.AttachmentURL);

                var uploadsFolder = Path.Combine(
                    _environment.WebRootPath,
                    "uploads", "announcements");
                Directory.CreateDirectory(uploadsFolder);

                var fileName =
                    Guid.NewGuid().ToString() + ext;
                var filePath = Path.Combine(
                    uploadsFolder, fileName);

                using var stream = new FileStream(
                    filePath, FileMode.Create);
                await model.Photo.CopyToAsync(stream);

                announcement.AttachmentURL =
                    "/uploads/announcements/" + fileName;
            }

            // Reset to Draft if Faculty edits a rejected announcement
            if (IsFaculty() && announcement.ApprovalStatus == "Rejected")
            {
                announcement.ApprovalStatus = "Draft";
                announcement.ChairRejectionReason = null;
                announcement.RejectionReason = null;
            }

            // ─── Update fields ─────────────────
            announcement.Title = model.Title;
            announcement.Body = model.Body;
            announcement.CategoryID = model.CategoryID;
            announcement.FeedType = model.FeedType;
            announcement.Priority = model.Priority;
            announcement.IsEmergency = model.IsEmergency;
            announcement.ExpiresAt = model.ExpiresAt;
            announcement.UpdatedAt = DateTime.Now;

            // ─── Replace tags ──────────────────
            _context.AnnouncementTags
                .RemoveRange(announcement.AnnouncementTags);

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

            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Announcement updated successfully!";

            if (IsFaculty())
                return RedirectToAction("MyAnnouncements");

            return RedirectToAction("Index");
        }

        private void DeletePhotoFile(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            var path = Path.Combine(
                _environment.WebRootPath,
                url.TrimStart('/').Replace(
                    '/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
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

            if (!IsFaculty())
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
        //  Faculty submits draft for review
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
                .Include(a => a.AnnouncementTags)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.AuthorID == userID &&
                    (a.ApprovalStatus == "Draft" ||
                     a.ApprovalStatus == "Rejected"));

            if (announcement == null)
                return RedirectToAction("MyAnnouncements");

            if (!announcement.AnnouncementTags.Any())
            {
                TempData["Error"] =
                    "Please add at least one department tag before submitting.";
                return RedirectToAction("MyAnnouncements");
            }

            // Find faculty's primary department tag
            var primaryDept = await _context.UserDepartments
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID && ud.IsPrimary);

            if (primaryDept == null)
            {
                TempData["Error"] =
                    "No department assigned. Contact an administrator.";
                return RedirectToAction("MyAnnouncements");
            }

            // Find Chair Person in same department
            var reviewer = await _context.UserDepartments
                .Include(ud => ud.User)
                    .ThenInclude(u => u.Role)
                .Where(ud =>
                    ud.TagID == primaryDept.TagID &&
                    ud.User.Role.RoleName == "Chair Person" &&
                    ud.User.IsActive)
                .Select(ud => ud.User)
                .FirstOrDefaultAsync();

            string newApprovalStatus;

            if (reviewer != null)
            {
                newApprovalStatus = "PendingChair";
            }
            else
            {
                // Fall back to Dean
                reviewer = await _context.UserDepartments
                    .Include(ud => ud.User)
                        .ThenInclude(u => u.Role)
                    .Where(ud =>
                        ud.TagID == primaryDept.TagID &&
                        ud.User.Role.RoleName == "Dean" &&
                        ud.User.IsActive)
                    .Select(ud => ud.User)
                    .FirstOrDefaultAsync();

                if (reviewer == null)
                {
                    TempData["Error"] =
                        "No Chair Person or Dean found for your " +
                        "department. Contact an administrator.";
                    return RedirectToAction("MyAnnouncements");
                }

                newApprovalStatus = "PendingDean";
            }

            // Clear stale data from any prior rejected cycle
            announcement.ChairApprovedByID = null;
            announcement.ChairApprovedAt = null;
            announcement.ChairRejectionReason = null;
            announcement.ApprovedByID = null;
            announcement.ApprovedAt = null;
            announcement.RejectionReason = null;

            announcement.ApprovalStatus = newApprovalStatus;
            announcement.SubmittedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // In-app notification
            _ = _notificationService.SendAsync(
                reviewer.UserID,
                "AnnouncementReview",
                $"New announcement pending your review: {announcement.Title}",
                $"/Announcement/Review/{announcement.AnnouncementID}",
                announcement.AnnouncementID);

            // Email notification (fire-and-forget)
            _ = _emailService.SendEmailAsync(
                reviewer.Email,
                $"{reviewer.FirstName} {reviewer.LastName}",
                "EduConnect: Announcement Pending Review",
                $"<p>Hello {reviewer.FirstName},</p>" +
                $"<p>A new announcement requires your review: " +
                $"<strong>{announcement.Title}</strong></p>" +
                $"<p><a href='https://localhost:7135/Announcement/Review/" +
                $"{announcement.AnnouncementID}'>Click here to review</a></p>");

            TempData["Success"] =
                "Announcement submitted for review.";
            return RedirectToAction("MyAnnouncements");
        }

        // ═══════════════════════════════════════
        //  GET: /Announcement/ReviewQueue
        //  Chair Person / Dean pending review list
        // ═══════════════════════════════════════
        public async Task<IActionResult> ReviewQueue()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != "Chair Person" && roleName != "Dean")
                return RedirectToAction("Index");

            var userID = GetUserID();

            var primaryDept = await _context.UserDepartments
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID && ud.IsPrimary);

            if (primaryDept == null)
            {
                ViewBag.Announcements = new List<object>();
                ViewBag.Role = roleName;
                return View();
            }

            var pendingStatus = roleName == "Chair Person"
                ? "PendingChair"
                : "PendingDean";

            var announcements = await _context.Announcements
                .Include(a => a.Author)
                .Include(a => a.Category)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .Where(a =>
                    a.ApprovalStatus == pendingStatus &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == primaryDept.TagID))
                .OrderBy(a => a.SubmittedAt)
                .Select(a => new
                {
                    a.AnnouncementID,
                    a.Title,
                    a.FeedType,
                    a.SubmittedAt,
                    AuthorName = a.Author.FirstName
                                 + " " + a.Author.LastName,
                    CategoryName = a.Category.CategoryName,
                    Tags = a.AnnouncementTags
                        .Select(at => at.DepartmentTag.ShortName)
                        .ToList()
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
            if (roleName != "Chair Person" && roleName != "Dean")
                return RedirectToAction("Index");

            var userID = GetUserID();

            var primaryDept = await _context.UserDepartments
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID && ud.IsPrimary);

            if (primaryDept == null)
                return RedirectToAction("ReviewQueue");

            var expectedStatus = roleName == "Chair Person"
                ? "PendingChair"
                : "PendingDean";

            var announcement = await _context.Announcements
                .Include(a => a.Author)
                    .ThenInclude(u => u.Role)
                .Include(a => a.Category)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.ApprovalStatus == expectedStatus &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == primaryDept.TagID));

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            ViewBag.Role = roleName;
            return View(announcement);
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Approve/{id}
        //  Chair Person or Dean approves
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != "Chair Person" && roleName != "Dean")
                return RedirectToAction("Index");

            var userID = GetUserID();

            var primaryDept = await _context.UserDepartments
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID && ud.IsPrimary);

            if (primaryDept == null)
                return RedirectToAction("ReviewQueue");

            var expectedStatus = roleName == "Chair Person"
                ? "PendingChair"
                : "PendingDean";

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.ApprovalStatus == expectedStatus &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == primaryDept.TagID));

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            if (roleName == "Chair Person")
            {
                announcement.ApprovalStatus = "PendingDean";
                announcement.ChairApprovedByID = userID;
                announcement.ChairApprovedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // Find Dean in same department
                var dean = await _context.UserDepartments
                    .Include(ud => ud.User)
                        .ThenInclude(u => u.Role)
                    .Where(ud =>
                        ud.TagID == primaryDept.TagID &&
                        ud.User.Role.RoleName == "Dean" &&
                        ud.User.IsActive)
                    .Select(ud => ud.User)
                    .FirstOrDefaultAsync();

                if (dean != null)
                {
                    _ = _notificationService.SendAsync(
                        dean.UserID,
                        "AnnouncementReview",
                        $"Announcement forwarded for your review: {announcement.Title}",
                        $"/Announcement/Review/{announcement.AnnouncementID}",
                        announcement.AnnouncementID);

                    _ = _emailService.SendEmailAsync(
                        dean.Email,
                        $"{dean.FirstName} {dean.LastName}",
                        "EduConnect: Announcement Pending Your Approval",
                        $"<p>Hello {dean.FirstName},</p>" +
                        $"<p>An announcement approved by the Chair Person now requires " +
                        $"your review: <strong>{announcement.Title}</strong></p>" +
                        $"<p><a href='https://localhost:7135/Announcement/Review/" +
                        $"{announcement.AnnouncementID}'>Click here to review</a></p>");
                }

                TempData["Success"] =
                    "Announcement approved and forwarded to the Dean.";
            }
            else // Dean
            {
                announcement.ApprovalStatus = "Approved";
                announcement.ApprovedByID = userID;
                announcement.ApprovedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                var author = await _context.Users
                    .FindAsync(announcement.AuthorID);

                _ = _notificationService.SendAsync(
                    announcement.AuthorID,
                    "AnnouncementApproved",
                    $"Your announcement has been approved — you can now publish it",
                    "/Announcement/MyAnnouncements",
                    announcement.AnnouncementID);

                if (author != null)
                {
                    _ = _emailService.SendEmailAsync(
                        author.Email,
                        $"{author.FirstName} {author.LastName}",
                        "EduConnect: Announcement Approved",
                        $"<p>Hello {author.FirstName},</p>" +
                        $"<p>Your announcement <strong>{announcement.Title}</strong> " +
                        $"has been approved by the Dean. You can now publish it.</p>" +
                        $"<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
                        $"Go to My Announcements</a></p>");
                }

                TempData["Success"] =
                    "Announcement approved. Faculty has been notified.";
            }

            return RedirectToAction("ReviewQueue");
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Reject/{id}
        //  Chair Person or Dean rejects
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string rejectionReason)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != "Chair Person" && roleName != "Dean")
                return RedirectToAction("Index");

            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["Error"] =
                    "A rejection reason is required.";
                return RedirectToAction("Review", new { id });
            }

            var userID = GetUserID();

            var primaryDept = await _context.UserDepartments
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID && ud.IsPrimary);

            if (primaryDept == null)
                return RedirectToAction("ReviewQueue");

            var expectedStatus = roleName == "Chair Person"
                ? "PendingChair"
                : "PendingDean";

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.ApprovalStatus == expectedStatus &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == primaryDept.TagID));

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            announcement.ApprovalStatus = "Rejected";

            if (roleName == "Chair Person")
                announcement.ChairRejectionReason = rejectionReason;
            else
                announcement.RejectionReason = rejectionReason;

            await _context.SaveChangesAsync();

            var author = await _context.Users
                .FindAsync(announcement.AuthorID);
            var rejectedBy = roleName == "Chair Person"
                ? "the Chair Person"
                : "the Dean";

            _ = _notificationService.SendAsync(
                announcement.AuthorID,
                "AnnouncementRejected",
                $"Your announcement was rejected by {rejectedBy}",
                "/Announcement/MyAnnouncements",
                announcement.AnnouncementID);

            if (author != null)
            {
                _ = _emailService.SendEmailAsync(
                    author.Email,
                    $"{author.FirstName} {author.LastName}",
                    "EduConnect: Announcement Rejected",
                    $"<p>Hello {author.FirstName},</p>" +
                    $"<p>Your announcement <strong>{announcement.Title}</strong> " +
                    $"was rejected by {rejectedBy}.</p>" +
                    $"<p><strong>Reason:</strong> {rejectionReason}</p>" +
                    $"<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
                    $"Go to My Announcements to revise and resubmit</a></p>");
            }

            TempData["Success"] = "Announcement rejected. Faculty has been notified.";
            return RedirectToAction("ReviewQueue");
        }

        // ═══════════════════════════════════════
        //  POST: /Announcement/Publish/{id}
        //  Faculty self-publishes after approval
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!IsFaculty())
                return RedirectToAction("Index");

            var userID = GetUserID();

            var announcement = await _context.Announcements
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
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

            // Notify department members
            var tagIDs = announcement.AnnouncementTags
                .Select(at => at.TagID)
                .ToList();

            if (tagIDs.Any())
            {
                bool broadcastAll = announcement.AnnouncementTags
                    .Any(at =>
                        at.DepartmentTag.ShortName == "ALL" ||
                        at.DepartmentTag.ShortName == "Emergency");

                List<int> recipientIds;
                if (broadcastAll)
                {
                    recipientIds = await _context.Users
                        .Where(u => u.IsActive && u.UserID != userID)
                        .Select(u => u.UserID)
                        .ToListAsync();
                }
                else
                {
                    recipientIds = await _context.UserDepartments
                        .Where(ud => tagIDs.Contains(ud.TagID))
                        .Select(ud => ud.UserID)
                        .Distinct()
                        .Where(uid => uid != userID)
                        .ToListAsync();
                }

                if (recipientIds.Count > 0)
                {
                    await _notificationService.SendToManyAsync(
                        recipientIds,
                        "Announcement",
                        $"New announcement: {announcement.Title}",
                        $"/Announcement/Details/{announcement.AnnouncementID}",
                        announcement.AnnouncementID);
                }
            }

            TempData["Success"] =
                "Announcement published successfully!";
            return RedirectToAction("MyAnnouncements");
        }
    }
}