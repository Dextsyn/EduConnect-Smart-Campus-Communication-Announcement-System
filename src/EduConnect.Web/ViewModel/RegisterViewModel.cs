using EduConnect.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class RegisterViewModel : IPlacementForm
    {
        private string _firstName, _lastName;
        private string? _studentId;

        [Required(ErrorMessage = "First name is required")]
        [MaxLength(50)]
        [RegularExpression(PersonInput.NamePattern, ErrorMessage = PersonInput.NameError)]
        public string FirstName
        {
            get => _firstName;
            set => _firstName = PersonInput.NormalizeName(value)!;
        }

        [Required(ErrorMessage = "Last name is required")]
        [MaxLength(50)]
        [RegularExpression(PersonInput.NamePattern, ErrorMessage = PersonInput.NameError)]
        public string LastName
        {
            get => _lastName;
            set => _lastName = PersonInput.NormalizeName(value)!;
        }

        [MaxLength(10)]
        [NameSuffix]
        public string? Suffix { get; set; }

        // Only Adamson accounts can register: the student types the part
        // before the domain and the domain is fixed. A pasted full Adamson
        // address is accepted; any other "@" fails the pattern.
        public const string EmailDomain = "@adamson.edu.ph";

        private string? _emailUser;

        [Required(ErrorMessage = "Email is required")]
        [MaxLength(80)]
        [RegularExpression(@"^[A-Za-z0-9]+([._-][A-Za-z0-9]+)*$",
            ErrorMessage = "Enter only the part before @adamson.edu.ph (letters, numbers, dots, dashes or underscores).")]
        public string? EmailUser
        {
            get => _emailUser;
            set
            {
                var v = value?.Trim();
                if (v != null && v.EndsWith(EmailDomain, StringComparison.OrdinalIgnoreCase))
                    v = v[..^EmailDomain.Length];
                _emailUser = v;
            }
        }

        public string Email => (EmailUser + EmailDomain).ToLowerInvariant();

        [Required(ErrorMessage = "Password is required")]
        [MinLength(8, ErrorMessage =
            "Password must be at least 8 characters")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm your password")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage =
            "Passwords do not match")]
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "Student number is required")]
        [RegularExpression(PersonInput.StudentNumberPattern, ErrorMessage = PersonInput.StudentNumberError)]
        public string? StudentID
        {
            get => _studentId;
            set => _studentId = PersonInput.NormalizeStudentNumber(value);
        }

        // College and department are derived from the program by
        // IPlacementService; only ProgramID is required.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
        public List<College> Hierarchy { get; set; } = new();
    }
}