using System.ComponentModel.DataAnnotations;
using EduConnect.Web;
using EduConnect.Web.ViewModels;

namespace EduConnect.Tests
{
    public class PersonInputTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        private static bool Valid(object model, string property, object? value)
        {
            var errors = new List<ValidationResult>();
            return Validator.TryValidateProperty(value,
                new ValidationContext(model) { MemberName = property }, errors);
        }

        [Theory]
        [InlineData("Juan")]
        [InlineData("Ma. Carmela")]
        [InlineData("Dela Cruz")]
        [InlineData("De Leon-Santos")]
        [InlineData("O'Brien")]
        [InlineData("D’Angelo")]
        [InlineData("Sto. Niño")]
        [InlineData("Peña")]
        [InlineData("José")]
        [InlineData("Jr.")]
        public void Name_AcceptsRealFilipinoNames(string name)
        {
            Assert.True(Valid(new RegisterViewModel(), nameof(RegisterViewModel.FirstName), name));
            Assert.True(Valid(new AdminUserFormViewModel(), nameof(AdminUserFormViewModel.LastName), name));
        }

        [Theory]
        [InlineData("J0hn")]
        [InlineData("Juan2")]
        [InlineData("@na")]
        [InlineData("Juan!")]
        [InlineData("Juan_Carlos")]
        [InlineData("--")]
        [InlineData("-Juan")]
        [InlineData("Juan-")]
        [InlineData("Juan--Carlos")]
        [InlineData("'")]
        [InlineData(".")]
        [InlineData("Juan × Carlos")]
        public void Name_RejectsDigitsAndSymbols(string name)
        {
            Assert.False(Valid(new RegisterViewModel(), nameof(RegisterViewModel.LastName), name));
            Assert.False(Valid(new AdminUserFormViewModel(), nameof(AdminUserFormViewModel.FirstName), name));
        }

        [Fact]
        public void Name_IsTrimmedAndSpacesCollapsed()
        {
            var model = new RegisterViewModel { FirstName = "  Juan   Carlos " };

            Assert.Equal("Juan Carlos", model.FirstName);
        }

        [Theory]
        [InlineData("202312345", true)]
        [InlineData(" 202312345 ", true)]
        [InlineData("20231234", false)]
        [InlineData("2023123456", false)]
        [InlineData("2023-12345", false)]
        [InlineData("20231234a", false)]
        [InlineData("２０２３１２３４５", false)]   // full-width digits
        public void StudentNumber_IsExactlyNineDigits(string typed, bool valid)
        {
            var register = new RegisterViewModel { StudentID = typed };
            var admin = new AdminUserFormViewModel { StudentID = typed };

            Assert.Equal(valid, Valid(register, nameof(RegisterViewModel.StudentID), register.StudentID));
            Assert.Equal(valid, Valid(admin, nameof(AdminUserFormViewModel.StudentID), admin.StudentID));
        }

        [Fact]
        public void StudentNumber_IsOptionalOnAdminForms()
        {
            var admin = new AdminUserFormViewModel { StudentID = "  " };

            Assert.Null(admin.StudentID);
            Assert.True(Valid(admin, nameof(AdminUserFormViewModel.StudentID), admin.StudentID));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("Jr.", true)]
        [InlineData("III", true)]
        [InlineData("Esq.", false)]
        [InlineData("Jr", false)]
        public void Suffix_MustComeFromTheList(string? suffix, bool valid)
        {
            Assert.Equal(valid, Valid(new RegisterViewModel(), nameof(RegisterViewModel.Suffix), suffix));
            Assert.Equal(valid, Valid(new AdminUserFormViewModel(), nameof(AdminUserFormViewModel.Suffix), suffix));
            Assert.Equal(valid, Valid(new ProfileViewModel(), nameof(ProfileViewModel.Suffix), suffix));
        }

        [Theory]
        [InlineData("Passw0rd!", true)]
        [InlineData("abc123#xyz", true)]
        [InlineData("1234567$", true)]          // exactly 8
        [InlineData("Ab1!", false)]             // too short
        [InlineData("Pass1!x", false)]          // 7 characters
        [InlineData("Password!", false)]        // no number
        [InlineData("Password1", false)]        // no special character
        [InlineData("Password 1", false)]       // a space isn't a special character
        public void Password_EightCharsWithNumberAndSpecial(string password, bool valid)
        {
            Assert.Equal(valid, Valid(new RegisterViewModel(), nameof(RegisterViewModel.Password), password));
            Assert.Equal(valid, Valid(new AdminUserFormViewModel(), nameof(AdminUserFormViewModel.Password), password));
            Assert.Equal(valid, Valid(new ChangePasswordViewModel(), nameof(ChangePasswordViewModel.NewPassword), password));
            Assert.Equal(valid, Valid(new ResetPasswordViewModel(), nameof(ResetPasswordViewModel.NewPassword), password));
        }

        [Fact]
        public void Password_BlankOnEditUser_KeepsTheCurrentOne() =>
            Assert.True(Valid(new AdminUserFormViewModel(), nameof(AdminUserFormViewModel.Password), null));

        [Fact]
        public async Task StudentNumberTaken_IgnoresRejectedRegistrationsAndTheUserThemself()
        {
            var active = _db.AddUser(RoleNames.Student);
            active.StudentID = "202300001";
            var rejected = _db.AddUser(RoleNames.StudentPending, isActive: false);
            rejected.StudentID = "202300002";
            rejected.VerificationStatus = "Rejected";
            _db.Context.SaveChanges();

            var users = _db.NewContext().Users;
            Assert.True(await PersonInput.IsStudentNumberTakenAsync(users, "202300001"));
            Assert.False(await PersonInput.IsStudentNumberTakenAsync(users, "202300001", active.UserID));
            Assert.False(await PersonInput.IsStudentNumberTakenAsync(users, "202300002"));
            Assert.False(await PersonInput.IsStudentNumberTakenAsync(users, "202300003"));
        }
    }
}
