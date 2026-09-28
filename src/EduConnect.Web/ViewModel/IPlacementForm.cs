using EduConnect.Web.Models;

namespace EduConnect.Web.ViewModels
{
    // What _PlacementFields needs: the chosen levels and the active tree.
    public interface IPlacementForm
    {
        int? CollegeID { get; set; }
        int? DepartmentID { get; set; }
        int? ProgramID { get; set; }
        List<College> Hierarchy { get; set; }
    }
}
