using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public record HierarchyResult(bool Ok, string? Error)
    {
        public static HierarchyResult Success { get; } = new(true, null);
        public static HierarchyResult Fail(string error) => new(false, error);
    }

    // Every rule about adding, renaming, retiring and restoring colleges,
    // departments and programs. Controllers call this and show Error.
    public interface IHierarchyService
    {
        Task<List<College>> GetTreeAsync(bool includeRetired);

        Task<HierarchyResult> AddCollegeAsync(string? name, string? shortName, bool hasDepartments);
        Task<HierarchyResult> AddDepartmentAsync(int collegeId, string? name, string? shortName);
        Task<HierarchyResult> AddProgramAsync(int departmentId, string? name, string? shortName);

        Task<HierarchyResult> RenameCollegeAsync(int id, string? name, string? shortName);
        Task<HierarchyResult> RenameDepartmentAsync(int id, string? name, string? shortName);
        Task<HierarchyResult> RenameProgramAsync(int id, string? name, string? shortName);

        // active = false retires, active = true restores.
        Task<HierarchyResult> SetCollegeActiveAsync(int id, bool active);
        Task<HierarchyResult> SetDepartmentActiveAsync(int id, bool active);
        Task<HierarchyResult> SetProgramActiveAsync(int id, bool active);
    }
}
