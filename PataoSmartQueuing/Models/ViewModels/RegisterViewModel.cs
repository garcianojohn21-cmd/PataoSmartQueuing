using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.ViewModels
{
    public class RegisterViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        public string? MiddleName { get; set; }

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [StringLength(12, MinimumLength = 12,
            ErrorMessage = "LRN must be exactly 12 digits.")]
        [RegularExpression(@"^\d{12}$",
            ErrorMessage = "LRN must be exactly 12 numeric digits.")]
        public string LRN { get; set; } = string.Empty;

        [Required]
        public string GradeLevel { get; set; } = string.Empty;

        [Required]
        public string Strand { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [MinLength(8)]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*[0-9]).+$",
            ErrorMessage = "Password must contain at least 1 uppercase letter and 1 number.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password),
            ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        public string VerificationCode { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true",
            ErrorMessage = "You must accept terms.")]
        public bool AcceptTerms { get; set; }
    }
}