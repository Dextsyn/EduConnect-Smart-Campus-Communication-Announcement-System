using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web
{
    // The one definition of a valid person name, student number and name
    // suffix. Every form that takes them (Register, Add User, Edit User)
    // uses these: names through [PersonName], the rest through
    // [RegularExpression], so the same pattern also drives client-side
    // validation.
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

        // A name with a digit gets this instead of NameError, so the person
        // sees exactly what is wrong.
        public const string NameDigitsError = "Names can't contain numbers.";

        public static bool HasDigit(string? value) =>
            value != null && value.Any(c => c is >= '0' and <= '9');

        // [0-9], not \d: .NET's \d also matches non-ASCII digits.
        public const string StudentNumberPattern = @"^[0-9]{9}$";

        public const string StudentNumberError = "Student number must be exactly 9 digits.";

        public const string StudentNumberTaken = "This student number is already registered.";

        public const string SuffixError = "Choose a suffix from the list.";

        // Every password someone sets (Register, Add/Edit User, Change and
        // Reset and Set Password): at least 8 characters, with at least one
        // upper-case letter, one lower-case letter, one digit and one special
        // character (anything but a letter, digit or space). Login doesn't
        // apply it, so passwords set before the rule still work.
        public const string PasswordPattern = @"^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^A-Za-z0-9\s]).{8,}$";

        public const string PasswordError =
            "Password must be at least 8 characters and include an upper-case letter, a lower-case letter, a number and a special character (such as ! @ # $).";

        public const string PasswordHint =
            "At least 8 characters, with an upper-case letter, a lower-case letter, a number and a special character (such as ! @ # $).";

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

    // A first or last name: blank (left to [Required]), or matching
    // PersonInput.NamePattern. A name with a digit fails with NameDigitsError,
    // anything else that doesn't match with NameError. On the client the
    // "personname" rule (wwwroot/js/person-name-validation.js, and the
    // Register page's own script) applies the same two checks.
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PersonNameAttribute : ValidationAttribute, IClientModelValidator
    {
        private static readonly Regex Pattern = new(PersonInput.NamePattern);

        public PersonNameAttribute() : base(PersonInput.NameError) { }

        protected override ValidationResult? IsValid(object? value, ValidationContext context)
        {
            if (value is not string s || s.Length == 0) return ValidationResult.Success;
            if (PersonInput.HasDigit(s))
                return new ValidationResult(PersonInput.NameDigitsError, MemberNames(context));
            return Pattern.IsMatch(s)
                ? ValidationResult.Success
                : new ValidationResult(PersonInput.NameError, MemberNames(context));
        }

        private static string[]? MemberNames(ValidationContext context) =>
            context.MemberName == null ? null : new[] { context.MemberName };

        public void AddValidation(ClientModelValidationContext context)
        {
            Set(context, "data-val", "true");
            Set(context, "data-val-personname", PersonInput.NameError);
            Set(context, "data-val-personname-pattern", PersonInput.NamePattern);
            Set(context, "data-val-personname-digits", PersonInput.NameDigitsError);
        }

        private static void Set(ClientModelValidationContext context, string key, string value) =>
            context.Attributes.TryAdd(key, value);
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
