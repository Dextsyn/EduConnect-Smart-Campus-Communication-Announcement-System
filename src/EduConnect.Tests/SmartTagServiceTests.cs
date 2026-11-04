using System.Text.Json;
using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class SmartTagServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit;
        private readonly Department _itis;
        private readonly Department _cs;
        private readonly AcademicProgram _bsit;
        private readonly AcademicProgram _bscs;
        private readonly User _faculty;
        private readonly int _academic;
        private readonly int _financial;
        private static readonly DateTime Today = new(2026, 11, 3, 9, 0, 0);

        public SmartTagServiceTests()
        {
            _ccit = _db.AddCollege("College of Computing and Information Technology");
            _itis = _db.AddDepartment(_ccit, "Information Technology and Information Systems");
            _cs = _db.AddDepartment(_ccit, "Computer Science");
            _bsit = _db.AddProgram(_itis, "BS Information Technology");
            _bsit.ShortName = "BSIT";
            _bscs = _db.AddProgram(_cs, "BS Computer Science");
            _bscs.ShortName = "BSCS";
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);

            var academic = new AnnouncementCategory { CategoryName = "Academic", Description = "Class schedules, exams, grades", FeedType = "Academic" };
            var financial = new AnnouncementCategory { CategoryName = "Financial", Description = "Payments, tuition, billing" };
            var emergency = new AnnouncementCategory { CategoryName = "Emergency", IsEmergency = true };
            _db.Context.AnnouncementCategories.AddRange(academic, financial, emergency);
            _db.Context.SaveChanges();
            _academic = academic.CategoryID;
            _financial = financial.CategoryID;
        }

        public void Dispose() => _db.Dispose();

        private sealed class FakeModel : ISmartTagModel
        {
            public string? Reply { get; set; }
            public string Name => "fake-model";
            public Task<SmartTagModelReply?> AskAsync(string systemPrompt, string userText) =>
                Task.FromResult(Reply == null ? null : new SmartTagModelReply(Reply, 42));
        }

        private SmartTagService Service(FakeModel model) =>
            new(_db.Context, new AudienceService(_db.Context), model, NullLogger<SmartTagService>.Instance)
            {
                Now = () => Today
            };

        [Fact]
        public async Task Ai_picks_outside_the_authors_scope_are_dropped()
        {
            var emergencyId = _db.Context.AnnouncementCategories.Single(c => c.CategoryName == "Emergency").CategoryID;
            var model = new FakeModel
            {
                Reply = JsonSerializer.Serialize(new
                {
                    categoryId = emergencyId,
                    priority = 7,
                    expiresAt = "2026-11-15T23:59",
                    summary = "BSIT and BSCS orientation on Nov 15.",
                    departmentIds = new[] { _cs.DepartmentID, _itis.DepartmentID },
                    programIds = new[] { _bsit.ProgramID, _bscs.ProgramID, 9999 },
                    audienceReason = "Names BSIT and BSCS."
                })
            };

            var s = await Service(model).SuggestAsync(_faculty.UserID, "Orientation", "BSIT and BSCS orientation on Nov 15.");

            Assert.Equal("ai", s.Source);
            Assert.Null(s.CategoryID);   // Emergency is never offered
            Assert.Null(s.Priority);     // out of range
            Assert.Equal(new DateTime(2026, 11, 15, 23, 59, 0), s.ExpiresAt);
            Assert.Equal(new[] { _itis.DepartmentID }, s.DepartmentIDs);   // Faculty: own department only
            Assert.Equal(new[] { _bsit.ProgramID }, s.ProgramIDs);
            Assert.Equal("BSIT and BSCS orientation on Nov 15.", s.Summary);

            var log = _db.NewContext().AIProcessingLogs.Single(l => l.AILogID == s.LogID);
            Assert.Equal(SmartTagService.ProcessType, log.ProcessType);
            Assert.Equal("fake-model", log.ModelUsed);
            Assert.Equal(42, log.TokensUsed);
            Assert.Null(log.AnnouncementID);
        }

        [Fact]
        public async Task Past_expiry_from_the_ai_is_dropped()
        {
            var model = new FakeModel { Reply = "{\"categoryId\": " + _academic + ", \"expiresAt\": \"2026-10-01T23:59\"}" };

            var s = await Service(model).SuggestAsync(_faculty.UserID, "Old exam notice", "Exams were on October 1.");

            Assert.Equal(_academic, s.CategoryID);
            Assert.Null(s.ExpiresAt);
        }

        [Fact]
        public async Task Without_the_ai_keyword_rules_fill_in()
        {
            var s = await Service(new FakeModel()).SuggestAsync(_faculty.UserID,
                "Tuition payment reminder",
                "BSIT students must settle their tuition payments until November 15. BSCS too.");

            Assert.Equal("rules", s.Source);
            Assert.Equal(new[] { _bsit.ProgramID }, s.ProgramIDs);   // BSCS is outside the Faculty member's scope
            Assert.Equal(_financial, s.CategoryID);
            Assert.Equal((byte)2, s.Priority);
            Assert.Equal(new DateTime(2026, 11, 15, 23, 59, 0), s.ExpiresAt);
            Assert.Null(s.Summary);
            Assert.Equal("rules", _db.NewContext().AIProcessingLogs.Single().ModelUsed);
        }

        [Fact]
        public async Task Unusable_ai_reply_falls_back_to_rules()
        {
            var s = await Service(new FakeModel { Reply = "not json" })
                .SuggestAsync(_faculty.UserID, "Urgent: class suspended", "All BSIT classes are suspended today.");

            Assert.Equal("rules", s.Source);
            Assert.Equal((byte)3, s.Priority);
        }

        [Fact]
        public async Task Outcome_links_the_run_and_records_what_was_kept()
        {
            var model = new FakeModel
            {
                Reply = JsonSerializer.Serialize(new
                {
                    categoryId = _academic,
                    priority = 2,
                    programIds = new[] { _bsit.ProgramID }
                })
            };
            var service = Service(model);
            var s = await service.SuggestAsync(_faculty.UserID, "Midterm exams", "BSIT midterm exams schedule.");
            var announcement = _db.AddAnnouncement(_faculty, "Midterm exams");

            // Poster kept the audience and priority but changed the category.
            await service.RecordOutcomeAsync(s.LogID!.Value, announcement.AnnouncementID, new SmartTagFinal(
                _financial, 2, null, null,
                Array.Empty<int>(), Array.Empty<int>(), new[] { _bsit.ProgramID }, Array.Empty<int>()));

            var ctx = _db.NewContext();
            Assert.Equal(announcement.AnnouncementID, ctx.AIProcessingLogs.Single(l => l.AILogID == s.LogID).AnnouncementID);
            var outcome = ctx.AIProcessingLogs.Single(l => l.ProcessType == SmartTagService.OutcomeProcessType);
            Assert.Equal(announcement.AnnouncementID, outcome.AnnouncementID);
            Assert.Equal(0.6667m, outcome.Confidence);
        }

        [Fact]
        public async Task Outcome_ignores_a_run_linked_to_another_announcement()
        {
            var service = Service(new FakeModel { Reply = "{\"priority\": 1}" });
            var s = await service.SuggestAsync(_faculty.UserID, "Library hours", "The library is open until 8 PM.");
            var first = _db.AddAnnouncement(_faculty, "First");
            var second = _db.AddAnnouncement(_faculty, "Second");
            var final = new SmartTagFinal(_academic, 1, null, null,
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

            await service.RecordOutcomeAsync(s.LogID!.Value, first.AnnouncementID, final);
            await service.RecordOutcomeAsync(s.LogID!.Value, second.AnnouncementID, final);

            var outcomes = _db.NewContext().AIProcessingLogs
                .Where(l => l.ProcessType == SmartTagService.OutcomeProcessType).ToList();
            Assert.Single(outcomes);
            Assert.Equal(first.AnnouncementID, outcomes[0].AnnouncementID);
        }
    }
}
