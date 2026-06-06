using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.Models.ViewModels
{
    public class StudentProfileViewModel
    {
        public int StudentID { get; set; }

        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? MiddleName { get; set; }

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string LRN { get; set; } = string.Empty;

        [Required]
        public string GradeLevel { get; set; } = string.Empty;

        [Required]
        public string Strand { get; set; } = string.Empty;

        public string ProfilePhoto { get; set; } = "/Images/ProfilePic.jfif";

        [Display(Name = "Upload Profile Photo")]
        public IFormFile? ProfileImage { get; set; }
    }
}