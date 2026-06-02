using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ─── Core Tables ───────────────────────────
        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<TagType> TagTypes { get; set; }
        public DbSet<DepartmentTag> DepartmentTags { get; set; }
        public DbSet<AnnouncementCategory> AnnouncementCategories { get; set; }
        public DbSet<Announcement> Announcements { get; set; }

        // ─── Junction Tables ───────────────────────
        public DbSet<UserDepartment> UserDepartments { get; set; }
        public DbSet<AnnouncementTag> AnnouncementTags { get; set; }

        // ─── Feature Tables ────────────────────────
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<Event> Events { get; set; }

        // ─── AI & System Tables ────────────────────
        public DbSet<AIProcessingLog> AIProcessingLogs { get; set; }
        public DbSet<ChatbotConversation> ChatbotConversations { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        // Auth & Security
        public DbSet<PasswordResetToken> PasswordResetTokens
        { get; set; }

        // Organizations
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrgMember> OrgMembers { get; set; }
        public DbSet<OrgAnnouncement> OrgAnnouncements
        { get; set; }

        // Campus Safety
        public DbSet<IncidentReport> IncidentReports
        { get; set; }

        // Study Groups
        public DbSet<StudyGroup> StudyGroups { get; set; }
        public DbSet<StudyGroupMember> StudyGroupMembers
        { get; set; }

        // Groups (Group Finder)
        public DbSet<Group> Groups { get; set; }
        public DbSet<GroupMember> GroupMembers { get; set; }
        public DbSet<GroupMessage> GroupMessages { get; set; }

        // Add DbSets
        public DbSet<EventRegistration> EventRegistrations
        { get; set; }
        public DbSet<EventWaitlist> EventWaitlist
        { get; set; }

        // ─── Personalization ───────────────────
        public DbSet<UserAnnouncementInteraction> UserAnnouncementInteractions
        { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ─── Roles ─────────────────────────────
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.RoleID);
                entity.HasIndex(e => e.RoleName).IsUnique();
            });

            // ─── TagTypes ──────────────────────────
            modelBuilder.Entity<TagType>(entity =>
            {
                entity.HasKey(e => e.TagTypeID);
                entity.HasIndex(e => e.TypeName).IsUnique();
            });

            // ─── DepartmentTags ────────────────────
            modelBuilder.Entity<DepartmentTag>(entity =>
            {
                entity.HasKey(e => e.TagID);
                entity.HasIndex(e => e.TagName).IsUnique();
                entity.HasOne(e => e.TagType)
                      .WithMany(e => e.DepartmentTags)
                      .HasForeignKey(e => e.TagTypeID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Users ─────────────────────────────
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserID);
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasOne(e => e.Role)
                      .WithMany(e => e.Users)
                      .HasForeignKey(e => e.RoleID)
                      .OnDelete(DeleteBehavior.Restrict);

            });

            // ─── UserDepartments (Junction) ────────
            modelBuilder.Entity<UserDepartment>(entity =>
            {
                entity.HasKey(e => e.UserDepartmentID);
                entity.HasIndex(e => new { e.UserID, e.TagID }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany(e => e.UserDepartments)
                      .HasForeignKey(e => e.UserID);
                entity.HasOne(e => e.DepartmentTag)
                      .WithMany(e => e.UserDepartments)
                      .HasForeignKey(e => e.TagID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── AnnouncementCategories ────────────
            modelBuilder.Entity<AnnouncementCategory>(entity =>
            {
                entity.HasKey(e => e.CategoryID);
                entity.HasIndex(e => e.CategoryName).IsUnique();
            });

            // ─── Announcements ─────────────────────
            modelBuilder.Entity<Announcement>(entity =>
            {
                entity.HasKey(e => e.AnnouncementID);
                entity.HasOne(e => e.Author)
                      .WithMany(e => e.Announcements)
                      .HasForeignKey(e => e.AuthorID);
                entity.HasOne(e => e.Category)
                      .WithMany(e => e.Announcements)
                      .HasForeignKey(e => e.CategoryID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── AnnouncementTags (Junction) ───────
            modelBuilder.Entity<AnnouncementTag>(entity =>
            {
                entity.HasKey(e => e.AnnouncementTagID);
                entity.HasIndex(e => new { e.AnnouncementID, e.TagID }).IsUnique();
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.AnnouncementTags)
                      .HasForeignKey(e => e.AnnouncementID);
                entity.HasOne(e => e.DepartmentTag)
                      .WithMany(e => e.AnnouncementTags)
                      .HasForeignKey(e => e.TagID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Notifications ─────────────────────
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(e => e.NotificationID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.Notifications)
                      .HasForeignKey(e => e.UserID);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.Notifications)
                      .HasForeignKey(e => e.AnnouncementID)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Feedback ──────────────────────────
            modelBuilder.Entity<Feedback>(entity =>
            {
                entity.HasKey(e => e.FeedbackID);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.Feedbacks)
                      .HasForeignKey(e => e.AnnouncementID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.Feedbacks)
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Events ────────────────────────────
            modelBuilder.Entity<Event>(entity =>
            {
                entity.HasKey(e => e.EventID);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.Events)
                      .HasForeignKey(e => e.AnnouncementID);
                entity.HasOne(e => e.Organizer)
                      .WithMany(e => e.OrganizedEvents)
                      .HasForeignKey(e => e.OrganizerID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── AIProcessingLogs ──────────────────
            modelBuilder.Entity<AIProcessingLog>(entity =>
            {
                entity.HasKey(e => e.AILogID);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.AIProcessingLogs)
                      .HasForeignKey(e => e.AnnouncementID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── ChatbotConversations ──────────────
            modelBuilder.Entity<ChatbotConversation>(entity =>
            {
                entity.HasKey(e => e.ConversationID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.ChatbotConversations)
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── RefreshTokens ─────────────────────
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => e.TokenID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.RefreshTokens)
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── AuditLogs ─────────────────────────
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.LogID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.AuditLogs)
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Add inside OnModelCreating

            // User verifiedby relationship
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasOne(e => e.VerifiedBy)
                      .WithMany()
                      .HasForeignKey(e => e.VerifiedByID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Announcement approvedby relationship
            modelBuilder.Entity<Announcement>(entity =>
            {
                entity.HasOne(e => e.ApprovedBy)
                      .WithMany()
                      .HasForeignKey(e => e.ApprovedByID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── PasswordResetTokens ───────────────
            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(e => e.TokenID);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Organizations ─────────────────────
            modelBuilder.Entity<Organization>(entity =>
            {
                entity.HasKey(e => e.OrgID);
                entity.HasOne(e => e.CreatedBy)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.DepartmentTag)
                      .WithMany()
                      .HasForeignKey(e => e.DepartmentTagID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── OrgMembers ────────────────────────
            modelBuilder.Entity<OrgMember>(entity =>
            {
                entity.HasKey(e => e.MemberID);
                entity.HasIndex(e => new
                { e.OrgID, e.UserID }).IsUnique();
                entity.HasOne(e => e.Organization)
                      .WithMany(e => e.Members)
                      .HasForeignKey(e => e.OrgID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── OrgAnnouncements ──────────────────
            modelBuilder.Entity<OrgAnnouncement>(entity =>
            {
                entity.HasKey(e => e.OrgAnnouncementID);
                entity.HasOne(e => e.Organization)
                      .WithMany(e => e.Announcements)
                      .HasForeignKey(e => e.OrgID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PostedBy)
                      .WithMany()
                      .HasForeignKey(e => e.PostedByID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── IncidentReports ───────────────────
            modelBuilder.Entity<IncidentReport>(entity =>
            {
                entity.HasKey(e => e.ReportID);
                entity.HasOne(e => e.ReportedBy)
                      .WithMany()
                      .HasForeignKey(e => e.ReportedByID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.HandledBy)
                      .WithMany()
                      .HasForeignKey(e => e.HandledByID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── StudyGroups ───────────────────────
            modelBuilder.Entity<StudyGroup>(entity =>
            {
                entity.HasKey(e => e.GroupID);
                entity.HasOne(e => e.CreatedBy)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.DepartmentTag)
                      .WithMany()
                      .HasForeignKey(e => e.DepartmentTagID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── StudyGroupMembers ─────────────────
            modelBuilder.Entity<StudyGroupMember>(entity =>
            {
                entity.HasKey(e => e.MembershipID);
                entity.HasIndex(e => new
                { e.GroupID, e.UserID }).IsUnique();
                entity.HasOne(e => e.StudyGroup)
                      .WithMany(e => e.Members)
                      .HasForeignKey(e => e.GroupID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── EventRegistrations ────────────────
            modelBuilder.Entity<EventRegistration>(entity =>
            {
                entity.HasKey(e => e.RegistrationID);
                entity.HasIndex(e => new
                { e.EventID, e.UserID }).IsUnique();
                entity.HasOne(e => e.Event)
                      .WithMany(e => e.Registrations)
                      .HasForeignKey(e => e.EventID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── UserAnnouncementInteractions ─────
            modelBuilder.Entity<UserAnnouncementInteraction>(entity =>
            {
                entity.HasKey(e => e.InteractionID);
                entity.HasOne(e => e.User)
                      .WithMany(e => e.AnnouncementInteractions)
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.UserInteractions)
                      .HasForeignKey(e => e.AnnouncementID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── EventWaitlist ─────────────────────
            modelBuilder.Entity<EventWaitlist>(entity =>
            {
                entity.HasKey(e => e.WaitlistID);
                entity.HasIndex(e => new
                { e.EventID, e.UserID }).IsUnique();
                entity.HasOne(e => e.Event)
                      .WithMany(e => e.Waitlist)
                      .HasForeignKey(e => e.EventID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Update Event relationships ────────
            modelBuilder.Entity<Event>(entity =>
            {
                entity.HasKey(e => e.EventID);
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.Events)
                      .HasForeignKey(e => e.AnnouncementID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Organizer)
                      .WithMany(e => e.OrganizedEvents)
                      .HasForeignKey(e => e.OrganizerID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Groups ────────────────────────────
            modelBuilder.Entity<Group>(entity =>
            {
                entity.HasKey(e => e.GroupID);
                entity.HasOne(e => e.Creator)
                      .WithMany()
                      .HasForeignKey(e => e.CreatorID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── GroupMembers ──────────────────────
            modelBuilder.Entity<GroupMember>(entity =>
            {
                entity.HasKey(e => e.MembershipID);
                entity.HasIndex(e => new { e.GroupID, e.UserID }).IsUnique();
                entity.HasOne(e => e.Group)
                      .WithMany(e => e.Members)
                      .HasForeignKey(e => e.GroupID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── GroupMessages ─────────────────────
            modelBuilder.Entity<GroupMessage>(entity =>
            {
                entity.HasKey(e => e.MessageID);
                entity.HasOne(e => e.Group)
                      .WithMany(e => e.Messages)
                      .HasForeignKey(e => e.GroupID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Sender)
                      .WithMany()
                      .HasForeignKey(e => e.SenderID)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
