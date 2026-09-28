using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace EduConnect.Web.ViewModels
{
    public class ProfileViewModel : IPlacementForm
    {
        // Read-only display fields — rendered as disabled inputs, so
        // they never come back in the POST body. ValidateNever keeps
        // MVC's implicit-required check (from non-nullable reference
        // types) from failing ModelState on every submit.
        [ValidateNever]
        public string FullName { get; set; }
        public string? StudentID { get; set; }
        [ValidateNever]
        public string Email { get; set; }
        [ValidateNever]
        public string RoleName { get; set; }
        public string? ProfilePicturePath { get; set; }

        // Editable fields
        [MaxLength(10)]
        public string? Suffix { get; set; }

        [DataType(DataType.Upload)]
        public IFormFile? NewProfilePicture { get; set; }

        // Placement. Students edit it here; everyone else sees it read-only
        // (the admin places staff).
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }
        [ValidateNever]
        public List<EduConnect.Web.Models.College> Hierarchy { get; set; } = new();
        [ValidateNever]
        public bool CanEditProgram { get; set; }
        [ValidateNever]
        public string? PlacementText { get; set; }
    }
}
