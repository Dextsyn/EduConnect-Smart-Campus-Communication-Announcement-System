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

        public int? CollegeID { get; set; }


        public List<SelectListItem> CollegeOptions { get; set; } = new();
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

    // Admin → Organizations → Members & Officers.
    public class OrgMembersViewModel
    {
        public Organization Org { get; set; }
        public List<OrgMemberRow> Officers { get; set; } = new();
        public List<OrgMemberRow> Members { get; set; } = new();
    }

    public class OrgMemberRow
    {
        public int MemberID { get; set; }
        public int UserID { get; set; }
        public string Name { get; set; } = "";
        public string StudentID { get; set; } = "—";
        public string Email { get; set; } = "";
        public string Placement { get; set; } = "—";
        public string? Position { get; set; }
        public bool IsOfficer { get; set; }
        // False when the account was deactivated or is no longer a
        // verified Student: the row stays, but it grants nothing.
        public bool IsEligible { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}
