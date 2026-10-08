using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web
{
    // The one definition of a valid person name, student number and name
    // suffix. Every form that takes them (Register, Add User, Edit User,
    // Profile) uses these, through [RegularExpression] so the same pattern
    // also drives client-side validation.
    public static class PersonInput
    {
        // Letters, including accented Latin letters such as ñ and é, in words
        // joined by a space, hyphen, apostrophe or period (". " too, as in
        // "Ma. Carmela"), with an optional final period. No digits, and no
        // leading, trailing or doubled separators. An explicit range rather
        // than \p{L}: jQuery Validation compiles the pattern without the
        // Unicode flag, where \p{L} matches nothing.
        public const string NamePattern =
            @"^[A-Za-zÀ-ÖØ-öø-ÿ]+(?:(?:\. ?|[ '’-])[A-Za-zÀ-ÖØ-öø-ÿ]+)*\.?$";

        public const string NameError =
            "Use letters only. Spaces, hyphens (-), apostrophes (') and periods (.) are allowed between letters.";

        // [0-9], not \d: .NET's \d also matches non-ASCII digits.
        public const string StudentNumberPattern = @"^[0-9]{9}$";

        public const string StudentNumberError = "Student number must be exactly 9 digits.";

        public const string StudentNumberTaken = "This student number is already registered.";

        public const string SuffixError = "Choose a suffix from the list.";

        public static readonly IReadOnlyList<string> Suffixes =
            new[] { "Jr.", "Sr.", "II", "III", "IV", "V" };

        // Trims and collapses runs of whitespace, so "  Juan   Carlos " is
        // saved (and validated) as "Juan Carlos".
        public static string? NormalizeName(string? value) =>
            value == null ? null : Regex.Replace(value.Trim(), @"\s+", " ");

        // Blank means "none".
        public static string? NormalizeStudentNumber(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static string? NormalizeSuffix(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Lower-case without accents, for name search: "Peña" -> "pena".
        public static string FoldForSearch(string? value)
        {
            var decomposed = (value ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static bool IsStudentRole(string? roleName) =>
            roleName is RoleNames.Student or RoleNames.StudentPending;

        // A student number belongs to one account. A rejected registration
        // doesn't hold it, so the student can register again with it.
        public static Task<bool> IsStudentNumberTakenAsync(
            IQueryable<User> users, string studentNumber, int? exceptUserId = null) =>
            users.AnyAsync(u =>
                u.StudentID == studentNumber &&
                u.VerificationStatus != "Rejected" &&
                u.UserID != exceptUserId);
    }

    // Blank, or one of PersonInput.Suffixes. Forms offer a dropdown, so this
    // only fails for a tampered post and needs no client-side rule.
    public sealed class NameSuffixAttribute : ValidationAttribute
    {
        public NameSuffixAttribute() : base(PersonInput.SuffixError) { }

        public override bool IsValid(object? value) =>
            value is not string s || string.IsNullOrWhiteSpace(s) || PersonInput.Suffixes.Contains(s.Trim());
    }
}
