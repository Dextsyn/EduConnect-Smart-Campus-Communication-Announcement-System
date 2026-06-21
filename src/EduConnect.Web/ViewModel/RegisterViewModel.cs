using EduConnect.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "First name is required")]
        [MaxLength(100)]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [MaxLength(100)]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }

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

        [Required(ErrorMessage = "Student ID is required")]
        [MaxLength(50)]
        public string StudentID { get; set; }

        [Required(ErrorMessage = "Please select your department")]
        public int? DepartmentTagID { get; set; }

        // Only departments — no roles
        public List<DepartmentTag> Departments { get; set; }
            = new List<DepartmentTag>();
    }
}