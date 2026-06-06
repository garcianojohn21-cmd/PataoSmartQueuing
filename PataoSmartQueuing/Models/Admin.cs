using System;
using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.Models
{
    public class Admin
    {
        public int AdminID { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? MiddleName { get; set; }

        [Required]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = "Staff";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}