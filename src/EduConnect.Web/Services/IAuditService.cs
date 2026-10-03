namespace EduConnect.Web.Services
{
    // Areas shown on the Audit Log page (AuditLog.TableAffected).
    public static class AuditArea
    {
        public const string Users = "Users";
        public const string Colleges = "Colleges";
        public const string Departments = "Departments";
        public const string Programs = "Programs";

        public static readonly string[] All = { Users, Colleges, Departments, Programs };
    }

    // Who-did-what records for user and academic-structure changes.
    public interface IAuditService
    {
        // Queues one row on the shared DbContext, stamped with the signed-in
        // user and request IP. The caller's next SaveChangesAsync writes it,
        // so the record commits together with the change it describes.
        // oldValues/newValues are serialized to JSON.
        void Record(string action, string area, int? recordId, string summary,
            object? oldValues = null, object? newValues = null);
    }
}
