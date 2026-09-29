using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class AudienceServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit, _cos;
        private readonly Department _itis, _cs, _bio;
        private readonly AcademicProgram _bsit, _bsis, _bscs, _bsbio;
        private readonly DepartmentTag _all, _office;
        private readonly User _studentBsit, _faculty, _chair, _dean, _staff, _studentBio;

        public AudienceServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _bsis = _db.AddProgram(_itis, "BSIS");
            _bscs = _db.AddProgram(_cs, "BSCS");
            _cos = _db.AddCollege("COS");
            _bio = _db.AddDepartment(_cos, "Biology");
            _bsbio = _db.AddProgram(_bio, "BSBio");
            _all = _db.AddTag("ALL");
            _office = _db.AddTag("REG");

            _studentBsit = _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            _staff = _db.AddUser(RoleNames.Staff);
            _db.TagUser(_staff, _office);
            _studentBio = _db.AddUser(RoleNames.Student, _cos.CollegeID, _bio.DepartmentID, _bsbio.ProgramID);

            var author = _db.AddUser(RoleNames.Administrator);
            _db.Target(_db.AddAnnouncement(author, "P-BSIT"), p: _bsit);
            _db.Target(_db.AddAnnouncement(author, "D-ITIS"), d: _itis);
            _db.Target(_db.AddAnnouncement(author, "C-CCIT"), c: _ccit);
            _db.Target(_db.AddAnnouncement(author, "P-BSCS"), p: _bscs);
            _db.Target(_db.AddAnnouncement(author, "D-CS"), d: _cs);
            _db.Target(_db.AddAnnouncement(author, "C-COS"), c: _cos);
            _db.TagAnnouncement(_db.AddAnnouncement(author, "ALL"), _all);
            _db.TagAnnouncement(_db.AddAnnouncement(author, "OFFICE"), _office);
        }

        public void Dispose() => _db.Dispose();

        private AudienceService Service => new(_db.Context);

        private async Task<string[]> Visible(User u)
        {
            var viewer = await Service.GetViewerAsync(u.UserID);
            return await _db.NewContext().Announcements
                .Where(Service.VisibleTo(viewer))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();
        }

        private async Task<string[]> Addressed(User u)
        {
            var viewer = await Service.GetViewerAsync(u.UserID);
            return await _db.NewContext().Announcements
                .Where(Service.AddressedTo(viewer))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();
        }

        [Fact]
        public void Target_WithTwoLevels_IsRejectedByTheDatabase()
        {
            var a = _db.AddAnnouncement(_dean, "Two levels");

            Assert.Throws<DbUpdateException>(() => _db.Target(a, _ccit, _itis));
        }

        [Fact]
        public async Task GetViewer_LoadsPlacementAndTags()
        {
            var v = await Service.GetViewerAsync(_staff.UserID);

            Assert.Equal(RoleNames.Staff, v.RoleName);
            Assert.Equal(new[] { _office.TagID }, v.TagIDs);
        }

        [Fact]
        public async Task VisibleTo_Student_SeesProgramDepartmentCollegeAndSchoolWide() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_studentBsit));

        [Fact]
        public async Task VisibleTo_Student_SeesCollegeTarget() =>
            Assert.Contains("C-COS", await Visible(_studentBio));

        [Fact]
        public async Task VisibleTo_Faculty_SeesOwnDepartmentsProgramsButNotOthers() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_faculty));

        [Fact]
        public async Task VisibleTo_Chairperson_OnlyOwnDepartmentAndCollege() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_chair));

        [Fact]
        public async Task VisibleTo_Dean_EverythingInsideTheCollegeOnly() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-CS", "D-ITIS", "P-BSCS", "P-BSIT" }, await Visible(_dean));

        [Fact]
        public async Task VisibleTo_Staff_TagsAndSchoolWideOnly() =>
            Assert.Equal(new[] { "ALL", "OFFICE" }, await Visible(_staff));

        [Fact]
        public async Task VisibleTo_Author_AlwaysSeesOwnAnnouncement()
        {
            _db.Target(_db.AddAnnouncement(_faculty, "Mine"), p: _bsbio);

            Assert.Contains("Mine", await Visible(_faculty));
        }

        [Fact]
        public async Task AddressedTo_ExcludesSchoolWide() =>
            Assert.Equal(new[] { "C-CCIT", "D-ITIS", "P-BSIT" }, await Addressed(_studentBsit));

        private static TargetSelection Sel(int[]? c = null, int[]? d = null, int[]? p = null) =>
            new(c ?? Array.Empty<int>(), d ?? Array.Empty<int>(), p ?? Array.Empty<int>());

        [Fact]
        public async Task Options_Dean_WholeCollege()
        {
            var o = await Service.GetTargetOptionsAsync(_dean.UserID);

            Assert.True(o.CanTargetCollege);
            Assert.True(o.CanTargetDepartments);
            Assert.Equal(_ccit.CollegeID, o.College!.CollegeID);
            Assert.Equal(new[] { "CS", "IT&IS" }, o.College.Departments.Select(d => d.Name).OrderBy(n => n));
            Assert.Equal(3, o.College.Departments.SelectMany(d => d.Programs).Count());
        }

        [Fact]
        public async Task Options_Chairperson_OwnDepartmentOnly()
        {
            var o = await Service.GetTargetOptionsAsync(_chair.UserID);

            Assert.False(o.CanTargetCollege);
            Assert.True(o.CanTargetDepartments);
            var dept = Assert.Single(o.College!.Departments);
            Assert.Equal("IT&IS", dept.Name);
            Assert.Equal(new[] { "BSIS", "BSIT" }, dept.Programs.Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task Options_Faculty_ProgramsOnly()
        {
            var o = await Service.GetTargetOptionsAsync(_faculty.UserID);

            Assert.False(o.CanTargetCollege);
            Assert.False(o.CanTargetDepartments);
            Assert.Equal(new[] { "BSIS", "BSIT" }, o.College!.Departments.Single().Programs.Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task Options_RetiredProgram_NotOffered()
        {
            _bsis.IsActive = false;
            _db.Context.SaveChanges();

            var o = await Service.GetTargetOptionsAsync(_faculty.UserID);

            Assert.Equal("BSIT", o.College!.Departments.Single().Programs.Single().Name);
        }

        [Fact]
        public async Task Options_UnplacedFaculty_None()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);

            Assert.Null((await Service.GetTargetOptionsAsync(unplaced.UserID)).College);
        }

        [Fact]
        public async Task Validate_FacultyOwnProgram_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(p: new[] { _bsit.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_FacultyOtherDepartmentsProgram_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(p: new[] { _bscs.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_FacultyDepartment_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(d: new[] { _itis.DepartmentID }))).Ok);

        [Fact]
        public async Task Validate_FacultyCollege_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task Validate_ChairpersonOwnDepartment_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_chair.UserID, Sel(d: new[] { _itis.DepartmentID }))).Ok);

        [Fact]
        public async Task Validate_ChairpersonCollege_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_chair.UserID, Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task Validate_DeanCollegeAndOtherDepartmentsProgram_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_dean.UserID, Sel(c: new[] { _ccit.CollegeID }, p: new[] { _bscs.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_DeanOtherCollegesProgram_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_dean.UserID, Sel(p: new[] { _bsbio.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_EmptySelection_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_faculty.UserID, Sel())).Ok);

        private async Task<int[]> RecipientsOf(string title, int exclude = 0)
        {
            var id = await _db.NewContext().Announcements.Where(a => a.Title == title).Select(a => a.AnnouncementID).SingleAsync();
            return (await Service.GetRecipientIdsAsync(id, exclude)).OrderBy(x => x).ToArray();
        }

        private static int[] Ids(params User[] users) => users.Select(u => u.UserID).OrderBy(x => x).ToArray();

        [Fact]
        public async Task Recipients_ProgramTarget_OnlyItsStudents() =>
            Assert.Equal(Ids(_studentBsit), await RecipientsOf("P-BSIT"));

        [Fact]
        public async Task Recipients_DepartmentTarget_StudentsFacultyAndChair() =>
            Assert.Equal(Ids(_studentBsit, _faculty, _chair), await RecipientsOf("D-ITIS"));

        [Fact]
        public async Task Recipients_CollegeTarget_EveryonePlacedInIt() =>
            Assert.Equal(Ids(_studentBsit, _faculty, _chair, _dean), await RecipientsOf("C-CCIT"));

        [Fact]
        public async Task Recipients_OfficeTag_TaggedUsers() =>
            Assert.Equal(Ids(_staff), await RecipientsOf("OFFICE"));

        [Fact]
        public async Task Recipients_SchoolWide_AllActiveUsersButTheAuthor()
        {
            var all = await RecipientsOf("ALL", exclude: _dean.UserID);

            Assert.DoesNotContain(_dean.UserID, all);
            Assert.Contains(_studentBio.UserID, all);
            Assert.Contains(_staff.UserID, all);
        }

        [Fact]
        public async Task Recipients_SkipInactiveUsersAndAuthor()
        {
            var pending = _db.AddUser(RoleNames.StudentPending, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID, isActive: false);

            var ids = await RecipientsOf("P-BSIT", exclude: _studentBsit.UserID);

            Assert.DoesNotContain(pending.UserID, ids);
            Assert.DoesNotContain(_studentBsit.UserID, ids);
        }

        [Fact]
        public async Task Labels_AppendShortNamesWithoutDuplicates()
        {
            _bsit.ShortName = "BSIT";
            _ccit.ShortName = "CCIT";
            _db.Context.SaveChanges();
            var ids = await _db.NewContext().Announcements
                .Where(a => a.Title == "P-BSIT" || a.Title == "C-CCIT")
                .OrderBy(a => a.Title)
                .Select(a => a.AnnouncementID).ToListAsync();
            var rows = ids.Select(id => new AnnouncementTableViewModel { AnnouncementID = id, Tags = new List<string> { "CCIT" } }).ToList();

            await Service.AddTargetLabelsAsync(rows);

            Assert.Equal(new[] { "CCIT" }, rows[0].Tags);          // C-CCIT: already had its college tag
            Assert.Equal(new[] { "CCIT", "BSIT" }, rows[1].Tags);  // P-BSIT
        }

        [Fact]
        public async Task TargetNames_FullNames()
        {
            var id = await _db.NewContext().Announcements.Where(a => a.Title == "D-ITIS").Select(a => a.AnnouncementID).SingleAsync();

            Assert.Equal(new[] { "IT&IS" }, await Service.GetTargetNamesAsync(id));
        }

        [Fact]
        public async Task HasAudience_TargetOnly_True()
        {
            var a = _db.AddAnnouncement(_faculty, "Draft for BSIT");
            _db.Target(a, p: _bsit);

            Assert.True(await Service.HasAudienceAsync(a.AnnouncementID));
        }

        [Fact]
        public async Task HasAudience_Nothing_False() =>
            Assert.False(await Service.HasAudienceAsync(_db.AddAnnouncement(_faculty, "Empty").AnnouncementID));

        [Fact]
        public async Task Validate_ExistingOutOfScopeTarget_AllowedWhenKept() =>
            Assert.True((await Service.ValidateTargetsAsync(_chair.UserID,
                Sel(c: new[] { _ccit.CollegeID }),
                keep: Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task Validate_NewOutOfScopeTarget_StillFailsWhenOthersKept() =>
            Assert.False((await Service.ValidateTargetsAsync(_chair.UserID,
                Sel(c: new[] { _ccit.CollegeID }, p: new[] { _bscs.ProgramID }),
                keep: Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task OutOfScopeTargets_ListsOnlyWhatTheAuthorCannotPick()
        {
            var a = _db.AddAnnouncement(_chair, "Old college post");
            _db.Target(a, c: _ccit);
            _db.Target(a, d: _itis);

            var kept = await Service.GetOutOfScopeTargetsAsync(_chair.UserID, a.AnnouncementID);

            var only = Assert.Single(kept);
            Assert.Equal(("College", _ccit.CollegeID, "CCIT"), (only.Level, only.ID, only.Name));
        }

        [Fact]
        public async Task AudienceNames_LegacyCollegeTagNotRepeated()
        {
            var legacy = _db.AddTag("CCITTAG");
            _ccit.LegacyTagID = legacy.TagID;
            _db.Context.SaveChanges();
            var a = _db.AddAnnouncement(_dean, "Backfilled");
            _db.TagAnnouncement(a, legacy);
            _db.TagAnnouncement(a, _all);
            _db.Target(a, c: _ccit);

            var names = await Service.GetAudienceNamesAsync(a.AnnouncementID);

            Assert.Equal(new[] { "ALL tag", "CCIT" }, names.OrderBy(n => n));
        }

        [Fact]
        public async Task Labels_RetiredTargetIsMarked()
        {
            _bsit.IsActive = false;
            _bsit.ShortName = "BSIT";
            _db.Context.SaveChanges();
            var id = await _db.NewContext().Announcements.Where(a => a.Title == "P-BSIT").Select(a => a.AnnouncementID).SingleAsync();
            var rows = new List<AnnouncementTableViewModel> { new() { AnnouncementID = id } };

            await Service.AddTargetLabelsAsync(rows);

            Assert.Equal(new[] { "BSIT (retired)" }, rows[0].Tags);
        }

        [Fact]
        public async Task Validate_UnplacedDean_ExplainsPlacement()
        {
            var unplaced = _db.AddUser(RoleNames.Dean);

            var r = await Service.ValidateTargetsAsync(unplaced.UserID, Sel(c: new[] { _ccit.CollegeID }));

            Assert.Contains("not placed", r.Error);
        }

        [Fact]
        public async Task Validate_UnplacedAuthorKeepingExistingTargets_Succeeds()
        {
            var unplaced = _db.AddUser(RoleNames.Dean);
            var existing = Sel(c: new[] { _ccit.CollegeID });

            var r = await Service.ValidateTargetsAsync(unplaced.UserID, existing, keep: existing);

            Assert.True(r.Ok);
        }

        [Fact]
        public async Task Not_InvertsVisibleTo()
        {
            var viewer = await Service.GetViewerAsync(_studentBsit.UserID);

            var hidden = await _db.NewContext().Announcements
                .Where(AudienceService.Not(Service.VisibleTo(viewer)))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();

            Assert.Equal(new[] { "C-COS", "D-CS", "OFFICE", "P-BSCS" }, hidden);
        }
    }
}
