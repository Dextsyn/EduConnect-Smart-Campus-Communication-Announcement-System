using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EduConnect.Web.ViewModels
{
    public class ProfileViewModel
    {
        // Read-only display fields
        public string FullName { get; set; }
        public string? StudentID { get; set; }
        public string Email { get; set; }
        public string RoleName { get; set; }
        public string? ProfilePicturePath { get; set; }

        // Editable fields
        [MaxLength(10)]
        public string? Suffix { get; set; }

        [DataType(DataType.Upload)]
        public IFormFile? NewProfilePicture { get; set; }
    }
}
