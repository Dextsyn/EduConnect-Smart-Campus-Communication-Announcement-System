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
            return role == "Administrator" ||
                   role == "Dean" ||
                   role == "Chair Person";
        }

        private bool CanEditAnnouncement(Announcement a) =>
            GetRoleName() == "Administrator" ||
            a.AuthorID == GetUserID();

        private bool IsFaculty() =>
            GetRoleName() == "Faculty";

        private bool CanCreate()
        {
            var role = GetRoleName();
            return role == "Administrator" ||
                   role == "Dean" ||
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
    }
}