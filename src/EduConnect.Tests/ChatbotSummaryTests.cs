using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class ChatbotSummaryTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly User _student;
        private readonly List<Announcement> _visible = new();
        private readonly Announcement _otherCollege;

        public ChatbotSummaryTests()
        {
            var ccit = _db.AddCollege("CCIT");
            var itis = _db.AddDepartment(ccit, "IT&IS");
            var bsit = _db.AddProgram(itis, "BSIT");
            var cos = _db.AddCollege("COS");
            var bio = _db.AddDepartment(cos, "Biology");
            _student = _db.AddUser(RoleNames.Student, ccit.CollegeID, itis.DepartmentID, bsit.ProgramID);
            var author = _db.AddUser(RoleNames.Administrator);

            // Eight announcements for the student, one an hour apart; "Post 8"
            // is the newest. A stored AISummary keeps Gemini out of the test.
            var start = new DateTime(2026, 10, 1, 8, 0, 0);
            for (var i = 1; i <= 8; i++)
            {
                var a = _db.AddAnnouncement(author, $"Post {i}");
                a.PublishedAt = start.AddHours(i);
                a.AISummary = $"Summary of post {i}";
                _db.Target(a, c: ccit);
                _visible.Add(a);
            }

            // The newest of all, but addressed to another college.
            _otherCollege = _db.AddAnnouncement(author, "Biology only");
            _otherCollege.PublishedAt = start.AddHours(20);
            _otherCollege.AISummary = "Not for CCIT";
            _db.Target(_otherCollege, d: bio);
            _db.Context.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private ChatbotService Service => new(
            _db.Context,
            new ConfigurationBuilder().Build(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ChatbotService>.Instance,
            null!,
            new Google.GenAI.Client(apiKey: "test"),
            new AudienceService(_db.Context));

        private Task<string> Ask(string message) =>
            Service.SendMessageAsync(_student.UserID, RoleNames.Student, "session", message);

        [Fact]
        public async Task Menu_ListsOnlyTheSixLatestTargetedAnnouncements_NewestFirst()
        {
            var reply = await Ask("Summarize an announcement");

            var listed = reply.Split('\n')
                .Where(l => l.StartsWith("- ["))
                .Select(l => l[3..l.IndexOf(']')])
                .ToArray();
            Assert.Equal(new[] { "Post 8", "Post 7", "Post 6", "Post 5", "Post 4", "Post 3" }, listed);
        }

        [Fact]
        public async Task Latest_SummarizesTheNewestTargetedAnnouncement()
        {
            var reply = await Ask("Summarize the latest announcement");

            Assert.Contains("Summary of post 8", reply);
        }

        [Fact]
        public async Task ByTitle_OnlyMatchesTheSixLatest()
        {
            Assert.Contains("Summary of post 3", await Ask("Summarize Post 3"));

            var older = await Ask("Summarize Post 2");
            Assert.DoesNotContain("Summary of post 2", older);
            Assert.Contains("Which announcement", older);
        }

        // Tapping a menu chip sends its ID; the chip must keep working after
        // newer posts push it out of the six.
        [Fact]
        public async Task ById_StillResolvesATargetedAnnouncementOutsideTheSix()
        {
            var reply = await Ask($"Summarize announcement {_visible[0].AnnouncementID}");

            Assert.Contains("Summary of post 1", reply);
        }

        [Fact]
        public async Task ById_NeverResolvesAnAnnouncementTheUserIsNotTargetedFor()
        {
            var reply = await Ask($"Summarize announcement {_otherCollege.AnnouncementID}");

            Assert.DoesNotContain("Not for CCIT", reply);
            Assert.Contains("Which announcement", reply);
        }
    }
}
