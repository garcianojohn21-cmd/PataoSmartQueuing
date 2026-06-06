using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.ViewModels
{
    public class StudentSettingsViewModel
    {
        public int StudentID { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string? CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters long")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
        public string? ConfirmPassword { get; set; }

        [Display(Name = "Enable Email Notifications")]
        public bool EnableEmailNotifications { get; set; }

        [Display(Name = "Use Dark Mode")]
        public bool UseDarkMode { get; set; }
    }
}