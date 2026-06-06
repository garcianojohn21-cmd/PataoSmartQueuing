using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.Models
{
    public class Student
    {
        public int StudentID { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? MiddleName { get; set; }

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [StringLength(12, MinimumLength = 12)]
        [RegularExpression(@"^\d{12}$",
            ErrorMessage = "LRN must be exactly 12 digits.")]
        public string LRN { get; set; } = string.Empty;

        [Required]
        public string GradeLevel { get; set; } = string.Empty;

        [Required]
        public string Strand { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(255)]
        public string ProfilePhoto { get; set; } = "/Images/ProfilePic.jfif";

        public virtual ICollection<QueueStudent> QueueStudents { get; set; }
            = new List<QueueStudent>();

        public virtual ICollection<PushSubscription> PushSubscriptions { get; set; }
            = new List<PushSubscription>();
    }
}