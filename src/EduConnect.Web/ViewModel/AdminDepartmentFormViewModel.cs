using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduConnect.Web.ViewModels
{
    public class AdminDepartmentFormViewModel
    {
        public int TagID { get; set; }

        [Required(ErrorMessage = "Department name is required")]
        [MaxLength(50)]
        public string TagName { get; set; }

        [Required(ErrorMessage = "Short code is required")]
        [MaxLength(20)]
        public string ShortName { get; set; }

        [Required(ErrorMessage = "Type is required")]
        public int TagTypeID { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Color is required")]
        [RegularExpression("^#[0-9A-Fa-f]{6}$",
            ErrorMessage = "Color must be a 6-digit hex value like #1D4ED8")]
        [MaxLength(7)]
        public string ColorHex { get; set; } = "#1D4ED8";

        public bool IsActive { get; set; } = true;

        // True for the ALL / EMRG rows, whose ShortName the announcement
        // feed matches on literally. The view renders those fields
        // read-only and the controller re-asserts them on POST.
        public bool IsSystemDepartment { get; set; }

        // Populated by the controller for the dropdown
        public List<SelectListItem> TagTypes { get; set; } = new();
    }
}
