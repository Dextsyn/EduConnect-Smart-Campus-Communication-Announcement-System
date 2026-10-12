namespace EduConnect.Web
{
    // Values of Users.VerificationStatus. A self-registered student moves
    // EmailPending → Pending (email confirmed, waiting for ITC) → Verified
    // or Rejected. Accounts an admin creates start Verified.
    public static class VerificationStatuses
    {
        public const string EmailPending = "EmailPending";
        public const string Pending = "Pending";
        public const string Verified = "Verified";
        public const string Rejected = "Rejected";
    }
}
