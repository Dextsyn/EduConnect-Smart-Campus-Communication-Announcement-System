// ForgotPasswordViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email")]
        public string Email { get; set; }
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string Token { get; set; }

        public string? Email { get; set; }

        [Required(ErrorMessage =
            "New password is required")]
        [MinLength(8, ErrorMessage =
            "Password must be at least 8 characters")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [Required(ErrorMessage =
            "Please confirm your password")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage =
            "Passwords do not match")]
        public string ConfirmPassword { get; set; }
    }
}