using EduConnect.Web.Data;
using EduConnect.Web.Hubs;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.IO;

namespace EduConnect.Web.Controllers
{
    public class EventController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EventController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly IHubContext<EventHub> _eventHub;

        public EventController(
            ApplicationDbContext context,
            ILogger<EventController> logger,
            IWebHostEnvironment environment,
            IEmailService emailService,
            INotificationService notificationService,
            IHubContext<EventHub> eventHub)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
            _emailService = emailService;
            _notificationService = notificationService;
            _eventHub = eventHub;
        }

        // ─── Helpers ───────────────────────────
        private bool IsLoggedIn() =>
            HttpContext.Session
                .GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session
                .GetString("UserID"));

        private string GetRoleName() =>
            HttpContext.Session
                .GetString("RoleName");

        private bool CanManageEvents()
        {
            var role = GetRoleName();
            return role == "Faculty" ||
                   role == "Dean" ||
                   role == "Chair Person";
        }

        private bool CanScan()
        {
            var role = GetRoleName();
            return role == "Faculty" ||
                   role == "Dean" ||
                   role == "Chair Person";
        }

        private string GetBaseUrl() =>
            $"{Request.Scheme}://{Request.Host}";

        // ─── Generate QR Code ──────────────────
        private string GenerateQRCode(
            int registrationID,
            int eventID,
            int userID)
        {
            var qrData =
                $"{GetBaseUrl()}/Event/Scan/{registrationID}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator
                .CreateQrCode(qrData,
                    QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(
                qrCodeData);

            var qrBytes = qrCode.GetGraphic(10);

            var qrFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads", "qrcodes");
            Directory.CreateDirectory(qrFolder);

            var fileName = $"qr_reg{registrationID}.png";
            System.IO.File.WriteAllBytes(Path.Combine(qrFolder, fileName), qrBytes);

            return $"/uploads/qrcodes/{fileName}";
        }

        // ═══════════════════════════════════════
        //  GET: /Event
        //  List all upcoming events
        // ═══════════════════════════════════════
        public async Task<IActionResult> Index(
            string? searchQuery,
            string? filter,
            string? view)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            var query = _context.Events
                .Include(e => e.Organizer)
                .Include(e => e.Registrations)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(searchQuery))
                query = query.Where(e =>
                    e.EventTitle.Contains(searchQuery) ||
                    e.Description.Contains(searchQuery) ||
                    e.Location.Contains(searchQuery));

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(e =>
                        e.StartDateTime > DateTime.Now);
                    break;
                case "today":
                    query = query.Where(e =>
                        e.StartDateTime.Date
                            == DateTime.Today);
                    break;
                case "registered":
                    query = query.Where(e =>
                        e.Registrations.Any(r =>
                            r.UserID == userID &&
                            r.Status == "Registered"));
                    break;
                case "past":
                    query = query.Where(e =>
                        e.EndDateTime < DateTime.Now);
                    break;
                default:
                    // Show upcoming by default
                    query = query.Where(e =>
                        e.EndDateTime >= DateTime.Now);
                    break;
            }

            var events = await query
                .OrderBy(e => e.StartDateTime)
                .ToListAsync();

            // Get user's registered event IDs
            var registeredEventIDs = await _context
                .EventRegistrations
                .Where(r =>
                    r.UserID == userID &&
                    r.Status == "Registered")
                .Select(r => r.EventID)
                .ToListAsync();

            // Build view models
            var eventList = events.Select(e =>
                new EventListViewModel
                {
                    EventID = e.EventID,
                    EventTitle = e.EventTitle,
                    Description = e.Description,
                    Location = e.Location,
                    CoverPhotoURL = e.CoverPhotoURL,
                    StartDateTime = e.StartDateTime,
                    EndDateTime = e.EndDateTime,
                    MaxAttendees = e.MaxAttendees,
                    CurrentAttendees = e.Registrations
                        .Count(r =>
                            r.Status == "Registered"),
                    IsOnline = e.IsOnline,
                    Status = e.Status,
                    IsRegistrationOpen =
                        e.IsRegistrationOpen &&
                        e.StartDateTime > DateTime.Now &&
                        (e.RegistrationDeadline == null ||
                         e.RegistrationDeadline
                             > DateTime.Now),
                    IsRegistered = registeredEventIDs
                        .Contains(e.EventID),
                    IsFull = e.MaxAttendees.HasValue &&
                        e.Registrations.Count(r =>
                            r.Status == "Registered")
                        >= e.MaxAttendees.Value,
                    SlotsRemaining = e.MaxAttendees
                        .HasValue
                        ? e.MaxAttendees.Value -
                          e.Registrations.Count(r =>
                              r.Status == "Registered")
                        : 999,
                    OrganizerName =
                        e.Organizer.FirstName + " " +
                        e.Organizer.LastName
                }).ToList();

            ViewBag.Events = eventList;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.Filter = filter ?? "all";
            ViewBag.ViewMode = view ?? "list";
            ViewBag.CanManage = CanManageEvents();

            // Calendar data — all events as JSON
            var calendarEvents = eventList.Select(e =>
                new
                {
                    id = e.EventID,
                    title = e.EventTitle,
                    start = e.StartDateTime
                        .ToString("yyyy-MM-ddTHH:mm:ss"),
                    end = e.EndDateTime
                        .ToString("yyyy-MM-ddTHH:mm:ss"),
                    color = e.IsRegistered
                        ? "#198754"
                        : e.IsFull
                            ? "#dc3545"
                            : "#0d6efd",
                    url = $"/Event/Details/{e.EventID}"
                }).ToList();

            ViewBag.CalendarEvents =
                System.Text.Json.JsonSerializer
                    .Serialize(calendarEvents);

            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Event/Details/5
        // ═══════════════════════════════════════
        public async Task<IActionResult> Details(
            int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();
            var roleName = GetRoleName();

            var ev = await _context.Events
                .Include(e => e.Organizer)
                    .ThenInclude(u => u.Role)
                .Include(e => e.Announcement)
                .Include(e => e.Registrations)
                    .ThenInclude(r => r.User)
                        .ThenInclude(u => u.UserDepartments)
                            .ThenInclude(ud =>
                                ud.DepartmentTag)
                .Include(e => e.Waitlist)
                .FirstOrDefaultAsync(e =>
                    e.EventID == id);

            if (ev == null)
                return NotFound();

            // Check user registration status
            var userRegistration = ev.Registrations
                .FirstOrDefault(r =>
                    r.UserID == userID);

            var userWaitlist = ev.Waitlist
                .FirstOrDefault(w =>
                    w.UserID == userID);

            var registeredCount = ev.Registrations
                .Count(r => r.Status == "Registered");

            var isFull = ev.MaxAttendees.HasValue &&
                registeredCount >= ev.MaxAttendees.Value;

            var slotsRemaining = ev.MaxAttendees
                .HasValue
                ? ev.MaxAttendees.Value
                    - registeredCount
                : 999;

            // Determine registration status
            string regStatus = "Open";
            if (userRegistration != null)
                regStatus = "Registered";
            else if (userWaitlist != null)
                regStatus = "Waitlist";
            else if (!ev.IsRegistrationOpen ||
                     ev.StartDateTime <= DateTime.Now)
                regStatus = "Closed";
            else if (isFull)
                regStatus = "Full";

            var model = new EventDetailViewModel
            {
                EventID = ev.EventID,
                EventTitle = ev.EventTitle,
                Description = ev.Description,
                Location = ev.Location,
                CoverPhotoURL = ev.CoverPhotoURL,
                StartDateTime = ev.StartDateTime,
                EndDateTime = ev.EndDateTime,
                MaxAttendees = ev.MaxAttendees,
                CurrentAttendees = registeredCount,
                IsOnline = ev.IsOnline,
                MeetingURL = ev.MeetingURL,
                Status = ev.Status,
                IsRegistrationOpen =
                    ev.IsRegistrationOpen,
                RegistrationDeadline =
                    ev.RegistrationDeadline,
                CreatedAt = ev.CreatedAt,
                OrganizerName =
                    ev.Organizer.FirstName + " " +
                    ev.Organizer.LastName,
                OrganizerRole =
                    ev.Organizer.Role.RoleName,
                IsRegistered =
                    userRegistration != null,
                IsOnWaitlist =
                    userWaitlist != null,
                IsFull = isFull,
                SlotsRemaining = slotsRemaining,
                WaitlistPosition =
                    userWaitlist?.Position ?? 0,
                UserQRCode =
                    userRegistration?.QRCode,
                RegistrationStatus = regStatus,
                AnnouncementTitle =
                    ev.Announcement?.Title,
                AnnouncementID =
                    ev.AnnouncementID,
                Registrations = ev.Registrations
                    .Select(r =>
                        new EventRegistrationViewModel
                        {
                            RegistrationID =
                                r.RegistrationID,
                            StudentName =
                                r.User.FirstName + " " +
                                r.User.LastName,
                            StudentID =
                                r.User.StudentID ?? "—",
                            Email = r.User.Email,
                            Department =
                                r.User.UserDepartments
                                    .FirstOrDefault(
                                        ud => ud.IsPrimary)
                                    ?.DepartmentTag
                                    ?.ShortName ?? "—",
                            Status = r.Status,
                            QRCode = r.QRCode,
                            RegisteredAt = r.RegisteredAt
                        })
                    .ToList()
            };

            ViewBag.IsOrganizer =
                ev.OrganizerID == userID ||
                roleName == "Administrator";

            return View(model);
        }

        // ═══════════════════════════════════════
        //  GET: /Event/Create
        // ═══════════════════════════════════════
        public async Task<IActionResult> Create()
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            if (!CanManageEvents())
                return RedirectToAction("Index");

            var userID = GetUserID();

            // Load published announcements
            // to optionally link to event
            var model = new EventFormViewModel
            {
                Announcements = await _context
                    .Announcements
                    .Where(a =>
                        a.Status == "Published" &&
                        a.AuthorID == userID)
                    .OrderByDescending(a =>
                        a.PublishedAt)
                    .ToListAsync()
            };

            return View(model);
        }

        // ═══════════════════════════════════════
        //  POST: /Event/Create
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
    EventFormViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            if (!CanManageEvents())
                return RedirectToAction("Index");

            // ─── Manual date validation ────────────
            if (!model.StartDateTime.HasValue)
            {
                ModelState.AddModelError(
                    "StartDateTime",
                    "Start date is required.");
            }

            if (!model.EndDateTime.HasValue)
            {
                ModelState.AddModelError(
                    "EndDateTime",
                    "End date is required.");
            }

            if (model.StartDateTime.HasValue &&
                model.EndDateTime.HasValue)
            {
                if (model.EndDateTime.Value
                    <= model.StartDateTime.Value)
                {
                    ModelState.AddModelError(
                        "EndDateTime",
                        "End date must be after " +
                        "start date.");
                }

                if (model.StartDateTime.Value
                    <= DateTime.Now)
                {
                    ModelState.AddModelError(
                        "StartDateTime",
                        "Event must be scheduled " +
                        "in the future.");
                }
            }

            if (!ModelState.IsValid)
            {
                model.Announcements = await _context
                    .Announcements
                    .Where(a => a.Status == "Published")
                    .ToListAsync();
                return View(model);
            }

            // Continue with creating event...
            // Use .Value since they are nullable now

            // Handle cover photo upload
            string? coverPhotoURL = null;
            if (model.CoverPhoto != null &&
                model.CoverPhoto.Length > 0)
            {
                var allowedTypes = new[]
                {
                    ".jpg", ".jpeg",
                    ".png", ".gif", ".webp"
                };

                var extension = Path.GetExtension(
                    model.CoverPhoto.FileName)
                    .ToLowerInvariant();

                if (allowedTypes.Contains(extension) &&
                    model.CoverPhoto.Length
                        <= 5 * 1024 * 1024)
                {
                    var uploadsFolder = Path.Combine(
                        _environment.WebRootPath,
                        "uploads", "events");

                    Directory.CreateDirectory(
                        uploadsFolder);

                    var fileName =
                        Guid.NewGuid().ToString()
                        + extension;

                    var filePath = Path.Combine(
                        uploadsFolder, fileName);

                    using var stream = new FileStream(
                        filePath, FileMode.Create);
                    await model.CoverPhoto
                        .CopyToAsync(stream);

                    coverPhotoURL =
                        "/uploads/events/" + fileName;
                }
            }

            var ev = new Event
            {
                OrganizerID = GetUserID(),
                AnnouncementID =
                    model.LinkedAnnouncementID,
                EventTitle = model.EventTitle,
                Description = model.Description,
                Location = model.Location,
                CoverPhotoURL = coverPhotoURL,
                StartDateTime = model.StartDateTime!.Value,
                EndDateTime = model.EndDateTime!.Value,
                MaxAttendees = model.MaxAttendees,
                IsOnline = model.IsOnline,
                MeetingURL = model.MeetingURL,
                IsRegistrationOpen = true,
                RegistrationDeadline =
                    model.RegistrationDeadline,
                Status = "Upcoming",
                CreatedAt = DateTime.Now
            };

            _context.Events.Add(ev);
            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Event created successfully!";
            return RedirectToAction(
                "Details", new { id = ev.EventID });
        }

        // ═══════════════════════════════════════
        //  POST: /Event/Register/5
        //  Student registers for event
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(
            int eventID)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            var ev = await _context.Events
                .Include(e => e.Registrations)
                .Include(e => e.Waitlist)
                .FirstOrDefaultAsync(e =>
                    e.EventID == eventID);

            if (ev == null)
                return NotFound();

            // Check if already registered
            var existing = ev.Registrations
                .FirstOrDefault(r =>
                    r.UserID == userID);

            if (existing != null)
            {
                TempData["Error"] =
                    "You are already registered " +
                    "for this event.";
                return RedirectToAction(
                    "Details", new { id = eventID });
            }

            // Check if already on waitlist
            var onWaitlist = ev.Waitlist
                .FirstOrDefault(w =>
                    w.UserID == userID);

            if (onWaitlist != null)
            {
                TempData["Error"] =
                    "You are already on the " +
                    "waitlist for this event.";
                return RedirectToAction(
                    "Details", new { id = eventID });
            }

            // Check registration deadline
            if (ev.RegistrationDeadline.HasValue &&
                ev.RegistrationDeadline.Value
                    < DateTime.Now)
            {
                TempData["Error"] =
                    "Registration deadline has passed.";
                return RedirectToAction(
                    "Details", new { id = eventID });
            }

            // Check if registration is open
            if (!ev.IsRegistrationOpen)
            {
                TempData["Error"] =
                    "Registration is closed.";
                return RedirectToAction(
                    "Details", new { id = eventID });
            }

            var registeredCount = ev.Registrations
                .Count(r => r.Status == "Registered");

            bool isFull = ev.MaxAttendees.HasValue &&
                registeredCount >= ev.MaxAttendees.Value;

            if (isFull)
            {
                // Add to waitlist
                var waitlistPosition =
                    ev.Waitlist.Count + 1;

                var waitlistEntry = new EventWaitlist
                {
                    EventID = eventID,
                    UserID = userID,
                    Position = waitlistPosition,
                    Status = "Waiting",
                    JoinedAt = DateTime.Now
                };

                _context.EventWaitlist
                    .Add(waitlistEntry);
                await _context.SaveChangesAsync();

                TempData["Info"] =
                    $"Event is full. You have been " +
                    $"added to the waitlist at " +
                    $"position #{waitlistPosition}.";
            }
            else
            {
                // Register directly
                var registration = new EventRegistration
                {
                    EventID = eventID,
                    UserID = userID,
                    Status = "Registered",
                    RegisteredAt = DateTime.Now
                };

                _context.EventRegistrations
                    .Add(registration);
                await _context.SaveChangesAsync();

                // Generate QR Code
                var qrCodePath = GenerateQRCode(
                    registration.RegistrationID,
                    eventID, userID);

                registration.QRCode = qrCodePath;
                await _context.SaveChangesAsync();

                // Send real-time notification
                await _notificationService.SendAsync(
                    userID,
                    "EventRegistration",
                    $"You're registered for \"{ev.EventTitle}\"",
                    $"/Event/Details/{eventID}");

                // Broadcast updated attendee count to Details page viewers
                if (ev.MaxAttendees.HasValue)
                {
                    var newCount = await _context.EventRegistrations
                        .CountAsync(r => r.EventID == eventID && r.Status == "Registered");
                    await _eventHub.Clients
                        .Group($"event-{eventID}")
                        .SendAsync("UpdateAttendeeCount", newCount, ev.MaxAttendees.Value);
                }

                // Send confirmation email
                try
                {
                    var user = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.UserID == userID);

                    var emailBody = $@"
                    <div style='font-family: Arial;
                                max-width: 600px;
                                margin: 0 auto;'>
                        <div style='background: #0d6efd;
                                    padding: 30px;
                                    text-align: center;
                                    border-radius:
                                        8px 8px 0 0;'>
                            <h1 style='color: white;
                                        margin: 0;'>
                                EduConnect
                            </h1>
                        </div>
                        <div style='background: #f8f9fa;
                                    padding: 30px;
                                    border-radius:
                                        0 0 8px 8px;'>
                            <h2 style='color: #198754;'>
                                ✅ Registration Confirmed!
                            </h2>
                            <p>Hi {user?.FirstName},</p>
                            <p>You have successfully
                               registered for:</p>
                            <div style='background: white;
                                        padding: 20px;
                                        border-radius: 8px;
                                        border-left: 4px
                                            solid #0d6efd;
                                        margin: 20px 0;'>
                                <h3 style='margin: 0 0
                                            10px;'>
                                    {ev.EventTitle}
                                </h3>
                                <p style='margin: 5px 0;
                                          color: #666;'>
                                    📅 {ev.StartDateTime
                                        .ToString(
                                        "MMMM dd, yyyy")}
                                </p>
                                <p style='margin: 5px 0;
                                          color: #666;'>
                                    🕐 {ev.StartDateTime
                                        .ToString(
                                        "hh:mm tt")} —
                                    {ev.EndDateTime
                                        .ToString(
                                        "hh:mm tt")}
                                </p>
                                <p style='margin: 5px 0;
                                          color: #666;'>
                                    📍 {ev.Location
                                        ?? "Online"}
                                </p>
                            </div>
                            <p style='color: #666;'>
                                Your QR code has been
                                generated. Please present
                                it at the event entrance
                                for attendance tracking.
                            </p>
                            <div style='text-align: center;
                                        margin: 20px 0;'>
                                <a href='{GetBaseUrl()}/Event/Details/{eventID}'
                                   style='background: #0d6efd;
                                           color: white;
                                           padding: 12px 24px;
                                           text-decoration:
                                               none;
                                           border-radius:
                                               6px;'>
                                    View My QR Code
                                </a>
                            </div>
                        </div>
                    </div>";

                    await _emailService.SendEmailAsync(
                        user?.Email ?? "",
                        user?.FirstName + " " +
                            user?.LastName,
                        $"Registration Confirmed: " +
                        $"{ev.EventTitle}",
                        emailBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Registration email failed: " +
                        "{Error}", ex.Message);
                }

                TempData["Success"] =
                    "Successfully registered! " +
                    "Check your email for your QR code.";
            }

            return RedirectToAction(
                "Details", new { id = eventID });
        }

        // ═══════════════════════════════════════
        //  POST: /Event/CancelRegistration
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            CancelRegistration(int eventID)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            var registration = await _context
                .EventRegistrations
                .FirstOrDefaultAsync(r =>
                    r.EventID == eventID &&
                    r.UserID == userID);

            if (registration != null)
            {
                registration.Status = "Cancelled";
                registration.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // Check waitlist — notify first person
                var firstWaiting = await _context
                    .EventWaitlist
                    .Include(w => w.User)
                    .Where(w =>
                        w.EventID == eventID &&
                        w.Status == "Waiting")
                    .OrderBy(w => w.Position)
                    .FirstOrDefaultAsync();

                if (firstWaiting != null)
                {
                    firstWaiting.Status = "Notified";
                    await _context.SaveChangesAsync();

                    // Send real-time notification
                    var waitlistEv = await _context.Events
                        .FirstOrDefaultAsync(e => e.EventID == eventID);
                    await _notificationService.SendAsync(
                        firstWaiting.UserID,
                        "WaitlistPromotion",
                        $"A spot opened for \"{waitlistEv?.EventTitle}\" — claim it within 24 hours!",
                        $"/Event/Details/{eventID}");

                    // Notify them via email
                    try
                    {
                        var ev = await _context.Events
                            .FirstOrDefaultAsync(e =>
                                e.EventID == eventID);

                        await _emailService
                            .SendEmailAsync(
                            firstWaiting.User.Email,
                            firstWaiting.User.FirstName,
                            "A slot is available! " +
                            $"{ev?.EventTitle}",
                            $@"<div style='font-family:
                                Arial; padding: 20px;'>
                                <h2>Good news!</h2>
                                <p>Hi {firstWaiting
                                    .User.FirstName},</p>
                                <p>A slot has opened up
                                   for <strong>
                                   {ev?.EventTitle}
                                   </strong>.</p>
                                <p>Please login to
                                   EduConnect and
                                   register within
                                   <strong>24 hours
                                   </strong> or the
                                   slot will go to
                                   the next person
                                   on the waitlist.
                                </p>
                                <a href='{GetBaseUrl()}/Event/Details/{eventID}'
                                   style='background:
                                       #0d6efd;
                                       color: white;
                                       padding: 12px 24px;
                                       text-decoration:
                                           none;
                                       border-radius:
                                           6px;'>
                                    Register Now
                                </a>
                            </div>");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            "Waitlist email failed: " +
                            "{Error}", ex.Message);
                    }
                }

                // Broadcast updated attendee count to Details page viewers
                var evMaxAtt = await _context.Events
                    .Where(e => e.EventID == eventID)
                    .Select(e => e.MaxAttendees)
                    .FirstOrDefaultAsync();
                if (evMaxAtt.HasValue)
                {
                    var registeredCount = await _context.EventRegistrations
                        .CountAsync(r => r.EventID == eventID && r.Status == "Registered");
                    await _eventHub.Clients
                        .Group($"event-{eventID}")
                        .SendAsync("UpdateAttendeeCount", registeredCount, evMaxAtt.Value);
                }

                TempData["Success"] =
                    "Registration cancelled. " +
                    "The next person on the " +
                    "waitlist has been notified.";
            }

            return RedirectToAction(
                "Details", new { id = eventID });
        }

        // ═══════════════════════════════════════
        //  POST: /Event/MarkAttendance
        //  Mark student as attended
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            MarkAttendance(
                int registrationID,
                int eventID,
                string? source = null)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            if (!CanManageEvents())
                return RedirectToAction(
                    "Details", new { id = eventID });

            var registration = await _context
                .EventRegistrations
                .FirstOrDefaultAsync(r =>
                    r.RegistrationID == registrationID);

            if (registration != null)
            {
                registration.Status = "Attended";
                registration.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            TempData["Success"] =
                "Attendance marked successfully.";

            if (source == "scan")
                return RedirectToAction(
                    "Scan", new { id = registrationID });

            return RedirectToAction(
                "Details", new { id = eventID });
        }

        // ═══════════════════════════════════════
        //  GET: /Event/Registrants/5
        //  Dedicated registrants list for organizer
        // ═══════════════════════════════════════
        public async Task<IActionResult> Registrants(
            int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();
            var roleName = GetRoleName();

            var ev = await _context.Events
                .Include(e => e.Organizer)
                    .ThenInclude(o => o.UserDepartments)
                .Include(e => e.Registrations)
                    .ThenInclude(r => r.User)
                        .ThenInclude(u => u.UserDepartments)
                            .ThenInclude(ud =>
                                ud.DepartmentTag)
                .Include(e => e.Waitlist)
                    .ThenInclude(w => w.User)
                        .ThenInclude(u => u.UserDepartments)
                            .ThenInclude(ud =>
                                ud.DepartmentTag)
                .FirstOrDefaultAsync(e =>
                    e.EventID == id);

            if (ev == null)
                return NotFound();

            bool canAccess =
                ev.OrganizerID == userID;

            if (!canAccess &&
                (roleName == "Dean" ||
                 roleName == "Chair Person"))
            {
                var userDeptTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                var organizerDeptTagIDs = ev.Organizer
                    .UserDepartments
                    .Select(ud => ud.TagID)
                    .ToList();

                canAccess = userDeptTagIDs
                    .Intersect(organizerDeptTagIDs)
                    .Any();
            }

            if (!canAccess)
                return RedirectToAction(
                    "Details", new { id });

            var model = new EventRegistrantsViewModel
            {
                EventID       = ev.EventID,
                EventTitle    = ev.EventTitle,
                StartDateTime = ev.StartDateTime,
                EndDateTime   = ev.EndDateTime,
                Location      = ev.Location,
                OrganizerName =
                    ev.Organizer.FirstName + " " +
                    ev.Organizer.LastName,
                RegisteredCount = ev.Registrations
                    .Count(r => r.Status == "Registered"),
                AttendedCount = ev.Registrations
                    .Count(r => r.Status == "Attended"),
                CancelledCount = ev.Registrations
                    .Count(r => r.Status == "Cancelled"),
                WaitlistedCount = ev.Waitlist.Count,
                Registrations = ev.Registrations
                    .OrderBy(r => r.RegisteredAt)
                    .Select(r =>
                        new EventRegistrationViewModel
                        {
                            RegistrationID =
                                r.RegistrationID,
                            StudentName =
                                r.User.FirstName + " " +
                                r.User.LastName,
                            StudentID =
                                r.User.StudentID ?? "—",
                            Email = r.User.Email,
                            Department =
                                r.User.UserDepartments
                                    .FirstOrDefault(
                                        ud => ud.IsPrimary)
                                    ?.DepartmentTag
                                    ?.ShortName ?? "—",
                            Status = r.Status,
                            QRCode = r.QRCode,
                            RegisteredAt = r.RegisteredAt
                        })
                    .ToList(),
                Waitlist = ev.Waitlist
                    .OrderBy(w => w.Position)
                    .Select(w =>
                        new WaitlistEntryViewModel
                        {
                            WaitlistID  = w.WaitlistID,
                            Position    = w.Position,
                            StudentName =
                                w.User.FirstName + " " +
                                w.User.LastName,
                            StudentID =
                                w.User.StudentID ?? "—",
                            Email    = w.User.Email,
                            Department =
                                w.User.UserDepartments
                                    .FirstOrDefault(
                                        ud => ud.IsPrimary)
                                    ?.DepartmentTag
                                    ?.ShortName ?? "—",
                            Status   = w.Status,
                            JoinedAt = w.JoinedAt
                        })
                    .ToList()
            };

            return View(model);
        }

        // ═══════════════════════════════════════
        //  GET: /Event/Scan/42
        //  QR code scan landing page
        // ═══════════════════════════════════════
        public async Task<IActionResult> Scan(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            if (!CanScan())
                return RedirectToAction("Index");

            var userID = GetUserID();
            var roleName = GetRoleName();

            var registration = await _context
                .EventRegistrations
                .Include(r => r.User)
                    .ThenInclude(u => u.UserDepartments)
                        .ThenInclude(ud => ud.DepartmentTag)
                .Include(r => r.Event)
                    .ThenInclude(e => e.Organizer)
                        .ThenInclude(o => o.UserDepartments)
                .FirstOrDefaultAsync(r =>
                    r.RegistrationID == id);

            if (registration == null)
                return NotFound();

            bool canAccess =
                registration.Event.OrganizerID == userID;

            if (!canAccess &&
                (roleName == "Dean" ||
                 roleName == "Chair Person"))
            {
                var userDeptTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                var organizerDeptTagIDs = registration
                    .Event.Organizer.UserDepartments
                    .Select(ud => ud.TagID)
                    .ToList();

                canAccess = userDeptTagIDs
                    .Intersect(organizerDeptTagIDs)
                    .Any();
            }

            if (!canAccess)
                return RedirectToAction(
                    "Details",
                    new { id = registration.EventID });

            var user = registration.User;
            var model = new ScanResultViewModel
            {
                RegistrationID     = registration.RegistrationID,
                RegistrationStatus = registration.Status,
                RegisteredAt       = registration.RegisteredAt,
                StudentFullName =
                    user.FirstName + " " + user.LastName,
                StudentID   = user.StudentID ?? "—",
                Email       = user.Email,
                Department  =
                    user.UserDepartments
                        .FirstOrDefault(ud => ud.IsPrimary)
                        ?.DepartmentTag?.ShortName ?? "—",
                EventID       = registration.Event.EventID,
                EventTitle    = registration.Event.EventTitle,
                StartDateTime = registration.Event.StartDateTime,
                EndDateTime   = registration.Event.EndDateTime,
                Location      = registration.Event.Location
            };

            return View(model);
        }

        // ═══════════════════════════════════════
        //  GET: /Event/Scanner
        //  In-app QR scanner page for faculty
        // ═══════════════════════════════════════
        public async Task<IActionResult> Scanner()
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            if (!CanScan())
                return RedirectToAction("Index");

            var events = await _context.Events
                .Where(e =>
                    e.StartDateTime.Date >= DateTime.Today &&
                    e.Status != "Cancelled")
                .OrderBy(e => e.StartDateTime)
                .Select(e => new
                {
                    e.EventID,
                    e.EventTitle,
                    Date = e.StartDateTime.ToString("MMM dd")
                })
                .ToListAsync();

            ViewBag.Events = events;
            return View();
        }

        // ═══════════════════════════════════════
        //  GET: /Event/ScanInfo/42
        //  JSON endpoint for scanner AJAX
        // ═══════════════════════════════════════
        public async Task<IActionResult> ScanInfo(int id)
        {
            if (!IsLoggedIn())
                return Json(new { error = "unauthorized" });

            if (!CanScan())
                return Json(new { error = "forbidden" });

            var registration = await _context
                .EventRegistrations
                .Include(r => r.User)
                    .ThenInclude(u => u.UserDepartments)
                        .ThenInclude(ud => ud.DepartmentTag)
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r =>
                    r.RegistrationID == id);

            if (registration == null)
                return Json(new { error = "not_found" });

            return Json(new
            {
                registrationID  = registration.RegistrationID,
                status          = registration.Status,
                registeredAt    = registration.RegisteredAt
                                    .ToString("MMMM dd, yyyy · h:mm tt"),
                studentFullName = registration.User.FirstName
                                  + " " + registration.User.LastName,
                studentID       = registration.User.StudentID ?? "—",
                email           = registration.User.Email,
                department      = registration.User.UserDepartments
                                    .FirstOrDefault(ud => ud.IsPrimary)
                                    ?.DepartmentTag?.ShortName ?? "—",
                eventID         = registration.Event.EventID,
                eventTitle      = registration.Event.EventTitle,
                startDateTime   = registration.Event.StartDateTime
                                    .ToString("MMMM dd, yyyy · h:mm tt"),
                location        = registration.Event.Location ?? "Online"
            });
        }

        // ═══════════════════════════════════════
        //  POST: /Event/MarkAttendanceAjax
        //  AJAX-friendly attendance marking
        // ═══════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAttendanceAjax(
            int registrationID)
        {
            if (!IsLoggedIn())
                return Json(new
                {
                    success = false,
                    error = "unauthorized"
                });

            if (!CanScan())
                return Json(new
                {
                    success = false,
                    error = "forbidden"
                });

            var registration = await _context
                .EventRegistrations
                .FirstOrDefaultAsync(r =>
                    r.RegistrationID == registrationID);

            if (registration == null)
                return Json(new
                {
                    success = false,
                    error = "not_found"
                });

            if (registration.Status == "Attended")
                return Json(new
                {
                    success = true,
                    alreadyAttended = true
                });

            registration.Status = "Attended";
            registration.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                alreadyAttended = false
            });
        }

        // ═══════════════════════════════════════
        //  GET: /Event/MyQRCode/5
        //  Show student's QR code
        // ═══════════════════════════════════════
        public async Task<IActionResult> MyQRCode(
            int eventID)
        {
            if (!IsLoggedIn())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            var registration = await _context
                .EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r =>
                    r.EventID == eventID &&
                    r.UserID == userID);

            if (registration == null)
                return RedirectToAction("Index");

            ViewBag.Registration = registration;
            return View();
        }
    }
}