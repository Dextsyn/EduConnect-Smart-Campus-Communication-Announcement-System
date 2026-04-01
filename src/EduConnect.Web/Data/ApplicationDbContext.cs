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
        }
    }
}
