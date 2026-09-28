using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
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
