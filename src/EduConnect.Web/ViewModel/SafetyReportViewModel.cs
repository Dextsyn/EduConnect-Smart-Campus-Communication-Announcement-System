using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class SafetyReportViewModel
    {
        [Required(ErrorMessage = "Please select a building.")]
        public string Building { get; set; }

        [MaxLength(255)]
        public string? SpecificLocation { get; set; }

        [Required(ErrorMessage = "Please describe the issue.")]
        public string Description { get; set; }

        public IFormFile? Photo { get; set; }

        public bool IsAnonymous { get; set; }
    }

    public class SafetyReportFilterViewModel
    {
        public string? Building { get; set; }
        public string? Status { get; set; }
    }
}
