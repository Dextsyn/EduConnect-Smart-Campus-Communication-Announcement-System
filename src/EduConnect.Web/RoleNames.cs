namespace EduConnect.Web
{
    // Every authorization check compares the session's RoleName string,
    // so these values must match Roles.RoleName exactly. Renaming a role
    // means a migration plus a change here — never a literal elsewhere.
    public static class RoleNames
    {
        public const string Administrator = "Administrator";
        public const string Dean = "Dean";
        public const string Chairperson = "Chairperson";
        public const string Faculty = "Faculty";
        public const string Staff = "Staff";
        public const string Student = "Student";
        public const string StudentPending = "Student Pending";

        // Role names that no longer exist. A session still carrying one
        // is cleared so the user logs in again and picks up the new name.
        public static readonly IReadOnlySet<string> Legacy =
            new HashSet<string> { "Chair Person" };
    }
}
