using System.ComponentModel.DataAnnotations;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduConnect.Web.ViewModels
{
    public class AdminUserFormViewModel : IPlacementForm
    {
        public int UserID { get; set; }

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

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [MaxLength(100)]
        public string Email { get; set; }

        // Leave blank on edit to keep existing password
        [DataType(DataType.Password)]
        [RegularExpression(PersonInput.PasswordPattern, ErrorMessage = PersonInput.PasswordError)]
        public string? Password { get; set; }

        // Students only. Optional here, but 9 digits when given.
        [RegularExpression(PersonInput.StudentNumberPattern, ErrorMessage = PersonInput.StudentNumberError)]
        public string? StudentID
        {
            get => _studentId;
            set => _studentId = PersonInput.NormalizeStudentNumber(value);
        }

        [Required(ErrorMessage = "Role is required")]
        public int RoleID { get; set; }

        // Holding the School Wide ("ALL") tag is what lets an author post
        // campus-wide announcements.
        public bool CanPostSchoolWide { get; set; }

        // Academic placement; which levels apply depends on the role.
        // IPlacementService validates and derives the rest.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        public bool IsActive { get; set; } = true;

        // Populated by the controller for the dropdowns
        public List<SelectListItem> Roles { get; set; } = new();
        public List<College> Hierarchy { get; set; } = new();
    }
}
