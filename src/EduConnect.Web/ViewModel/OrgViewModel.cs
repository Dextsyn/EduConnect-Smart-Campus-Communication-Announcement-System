using EduConnect.Web;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    // GET /Org — the organization directory. Students see the orgs they
    // follow or belong to first, then every other org to discover.
    // Post titles appear only for orgs the viewer may read.
    public class OrgDirectoryViewModel
    {
        public List<OrgDirectoryRow> Following { get; set; } = new();
        public List<OrgDirectoryRow> Discover { get; set; } = new();
        public bool CanFollow { get; set; }
        public int? OfficerOrgID { get; set; }
    }

    public class OrgDirectoryRow
    {
        public Organization Org { get; set; }
        public int FollowerCount { get; set; }
        public string? LatestTitle { get; set; }
        public string Relation { get; set; } = OrgRelation.None;
    }

    // An org's officers (and admins) edit only its About Us text; the name,
    // logo and college stay on the admin Edit form.
    public class OrgAboutUsViewModel
    {
        public int OrgID { get; set; }
        public string OrgName { get; set; } = "";

        [MaxLength(2000, ErrorMessage = "About Us can be at most 2000 characters.")]
        public string? AboutUs { get; set; }
    }

    public class OrgFormViewModel
    {
        [Required(ErrorMessage = "Organization name is required")]
        [MaxLength(200)]
        public string OrgName { get; set; }

        [MaxLength(2000)]
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

        // One inline photo (JPG, PNG or WEBP). Posts take no file
        // attachments.
        public IFormFile? Photo { get; set; }
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
