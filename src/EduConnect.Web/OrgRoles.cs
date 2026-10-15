namespace EduConnect.Web
{
    // OrgMembers.OrgRole values — compare against these, never a literal.
    // An officer is a Student with an Officer row, not a role of its own,
    // so officers keep everything else a Student has (feed, sanctions).
    public static class OrgRoles
    {
        public const string Member = "Member";
        public const string Officer = "Officer";

        // The former Faculty adviser. Rows are kept inactive so old posts
        // keep their history; nothing grants it any more.
        public const string Adviser = "Adviser";
    }
}
