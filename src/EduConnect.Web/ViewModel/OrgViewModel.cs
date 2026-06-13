using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class OrgFeedViewModel
    {
        public List<OrgFeedGroup> Groups { get; set; } = new();
        public int? FilterOrgID { get; set; }
        public List<SelectListItem> OrgOptions { get; set; } = new();
    }

    public class OrgFeedGroup
    {
        public Organization Org { get; set; }
        public List<OrgAnnouncement> Announcements { get; set; } = new();
    }

    public class OrgFormViewModel
    {
        [Required(ErrorMessage = "Organization name is required")]
        [MaxLength(200)]
        public string OrgName { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public IFormFile? Logo { get; set; }
        public string? ExistingLogoURL { get; set; }

        public int? DepartmentTagID { get; set; }

        [Required(ErrorMessage = "Please select a Faculty adviser")]
        public int AdviserUserID { get; set; }

        public List<SelectListItem> FacultyOptions { get; set; } = new();
        public List<SelectListItem> DepartmentOptions { get; set; } = new();
    }

    public class OrgPostViewModel
    {
        public int OrgID { get; set; }
        public string OrgName { get; set; } = "";

        [Required(ErrorMessage = "Title is required")]
        [MaxLength(300)]
        public string Title { get; set; }

        [Required(ErrorMessage = "Body is required")]
        public string Body { get; set; }

        public IFormFile? Attachment { get; set; }
        public bool IsPinned { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
