using System.ComponentModel.DataAnnotations;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduConnect.Web.ViewModels
{
    public class AdminUserFormViewModel : IPlacementForm
    {
        public int UserID { get; set; }

        [Required(ErrorMessage = "First name is required")]
        [MaxLength(50)]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [MaxLength(50)]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [MaxLength(100)]
        public string Email { get; set; }

        // Leave blank on edit to keep existing password
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string? Password { get; set; }

        [MaxLength(50)]
        public string? StudentID { get; set; }

        [Required(ErrorMessage = "Role is required")]
        public int RoleID { get; set; }

        // Optional: School Wide or a non-academic office. Academic users are
        // reached through their placement.
        public int? DepartmentTagID { get; set; }

        // Academic placement; which levels apply depends on the role.
        // IPlacementService validates and derives the rest.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        public bool IsActive { get; set; } = true;

        // Populated by the controller for the dropdowns
        public List<SelectListItem> Roles { get; set; } = new();
        public List<SelectListItem> Departments { get; set; } = new();
        public List<College> Hierarchy { get; set; } = new();
    }
}
