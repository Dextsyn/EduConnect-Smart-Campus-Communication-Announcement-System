namespace EduConnect.Web.Services
{
    // Besides the organizer, a Dean manages the events of organizers in
    // their college and a Chairperson those of organizers in their
    // department.
    public static class EventAccess
    {
        public static bool ManagesEventsOf(string? role,
            int? myCollegeId, int? myDepartmentId,
            int? organizerCollegeId, int? organizerDepartmentId) =>
            role switch
            {
                RoleNames.Dean => myCollegeId != null && myCollegeId == organizerCollegeId,
                RoleNames.Chairperson => myDepartmentId != null && myDepartmentId == organizerDepartmentId,
                _ => false
            };
    }
}
