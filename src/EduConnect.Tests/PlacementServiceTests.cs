using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class PlacementServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit;
        private readonly Department _itis;
        private readonly AcademicProgram _bsit;
        private readonly College _cos;
        private readonly College _law;

        public PlacementServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _cos = _db.AddCollege("College of Science");
            _law = _db.AddCollege("College of Law", flat: true);
        }

        public void Dispose() => _db.Dispose();

        private PlacementService Service => new(_db.Context);

        private static User NewUser() => new();

        private Department LawDept => _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);

        [Fact]
        public async Task Student_Program_SetsAllThreeLevels()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
            Assert.Equal(_itis.DepartmentID, user.DepartmentID);
            Assert.Equal(_bsit.ProgramID, user.ProgramID);
        }

        [Fact]
        public async Task StudentPending_WithoutProgram_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.StudentPending, _ccit.CollegeID, _itis.DepartmentID, null);

            Assert.False(result.Ok);
            Assert.Contains("program", result.Error);
        }

        [Fact]
        public async Task Student_RetiredProgram_Fails()
        {
            _bsit.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Student_ProgramUnderRetiredCollege_Fails()
        {
            _ccit.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Student_ProgramWinsOverMismatchedCollege()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Student, _cos.CollegeID, null, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
        }

        [Fact]
        public async Task Faculty_Department_SetsCollegeAndDepartment()
        {
            var user = NewUser();
            user.ProgramID = _bsit.ProgramID;

            var result = await Service.ApplyAsync(user, RoleNames.Faculty, null, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
            Assert.Equal(_itis.DepartmentID, user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        [Fact]
        public async Task Faculty_FlatCollege_ResolvesImplicitDepartment()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Faculty, _law.CollegeID, null, null);

            Assert.True(result.Ok);
            Assert.Equal(_law.CollegeID, user.CollegeID);
            Assert.Equal(LawDept.DepartmentID, user.DepartmentID);
        }

        [Fact]
        public async Task Faculty_StructuredCollegeWithoutDepartment_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Faculty, _ccit.CollegeID, null, null);

            Assert.False(result.Ok);
            Assert.Contains("department", result.Error);
        }

        [Fact]
        public async Task Chairperson_FlatCollege_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Chairperson, _law.CollegeID, null, null);

            Assert.False(result.Ok);
            Assert.Contains("no departments", result.Error);
        }

        [Fact]
        public async Task Chairperson_RetiredDepartment_Fails()
        {
            _itis.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Chairperson, null, _itis.DepartmentID, null);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Dean_IgnoresPostedDepartmentAndProgram()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Dean, _cos.CollegeID, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_cos.CollegeID, user.CollegeID);
            Assert.Null(user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        [Fact]
        public async Task Dean_WithoutCollege_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Dean, null, null, null);

            Assert.False(result.Ok);
            Assert.Contains("college", result.Error);
        }

        [Fact]
        public async Task Staff_ClearsExistingPlacement()
        {
            var user = NewUser();
            user.CollegeID = _ccit.CollegeID;
            user.DepartmentID = _itis.DepartmentID;
            user.ProgramID = _bsit.ProgramID;

            var result = await Service.ApplyAsync(user, RoleNames.Staff, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Null(user.CollegeID);
            Assert.Null(user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        private List<(int TagID, bool IsPrimary)> TagsOf(int userId) =>
            _db.NewContext().UserDepartments
                .Where(ud => ud.UserID == userId)
                .Select(ud => new { ud.TagID, ud.IsPrimary })
                .AsEnumerable()
                .Select(x => (x.TagID, x.IsPrimary))
                .OrderBy(x => x.TagID)
                .ToList();

        private College CollegeWithTag(string name, string shortName)
        {
            var tag = _db.AddTag(shortName);
            var college = _db.AddCollege(name);
            college.LegacyTagID = tag.TagID;
            _db.Context.SaveChanges();
            return college;
        }

        [Fact]
        public async Task SyncFeedTag_NewStudent_AddsCollegeTagAsPrimary()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);

            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (cba.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_ChangedCollege_ReplacesPrimaryTag()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var coe = CollegeWithTag("COE", "COE");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            user.CollegeID = coe.CollegeID;
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (coe.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_CollegeWithoutLegacyTag_RemovesPrimaryTag()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            user.CollegeID = _cos.CollegeID;   // _cos has no LegacyTagID
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Empty(TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_ExistingNonPrimaryRowForNewTag_PromotesIt()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var coe = CollegeWithTag("COE", "COE");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            _db.Context.UserDepartments.Add(new UserDepartment { UserID = user.UserID, TagID = cba.LegacyTagID!.Value, IsPrimary = true });
            _db.Context.UserDepartments.Add(new UserDepartment { UserID = user.UserID, TagID = coe.LegacyTagID!.Value, IsPrimary = false });
            _db.Context.SaveChanges();

            user.CollegeID = coe.CollegeID;
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (coe.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_AlreadyCorrect_LeavesRowAlone()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();
            var before = _db.NewContext().UserDepartments.Single().UserDepartmentID;

            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(before, _db.NewContext().UserDepartments.Single().UserDepartmentID);
        }

        [Fact]
        public async Task Labels_UseTheMostSpecificPlacement()
        {
            _bsit.ShortName = "BSIT";
            _db.Context.SaveChanges();
            var student = _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            var faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, LawDept.DepartmentID);
            var tagged = _db.AddUser(RoleNames.Staff);
            _db.TagUser(tagged, _db.AddTag("REG"));
            var nothing = _db.AddUser(RoleNames.Staff);

            var labels = await Service.GetPlacementLabelsAsync(new[] { student.UserID, faculty.UserID, lawFaculty.UserID, tagged.UserID, nothing.UserID });

            Assert.Equal("BSIT", labels[student.UserID]);
            Assert.Equal("IT&IS", labels[faculty.UserID]);
            Assert.Equal("College of Law", labels[lawFaculty.UserID]);
            Assert.Equal("REG", labels[tagged.UserID]);
            Assert.Equal("—", labels[nothing.UserID]);
        }

        [Fact]
        public async Task NeedsPlacement_FlagsOnlyUsersMissingTheirRequiredLevel()
        {
            _db.AddUser(RoleNames.Student, _ccit.CollegeID);                                   // no program → flagged
            _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            _db.AddUser(RoleNames.Faculty, _ccit.CollegeID);                                   // no department → flagged
            _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            _db.AddUser(RoleNames.Dean);                                                        // no college → flagged
            _db.AddUser(RoleNames.Staff);

            var flagged = await _db.NewContext().Users
                .Include(u => u.Role)
                .Where(PlacementService.NeedsPlacement)
                .Select(u => u.Role.RoleName)
                .ToListAsync();

            Assert.Equal(new[] { RoleNames.Dean, RoleNames.Faculty, RoleNames.Student },
                flagged.OrderBy(r => r));
        }
    }
}
