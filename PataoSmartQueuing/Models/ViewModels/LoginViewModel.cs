using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.ViewModels
{
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string? Password { get; set; }

        public string? VerificationCode { get; set; }

        public bool UseVerificationCode { get; set; } = false;
    }
}