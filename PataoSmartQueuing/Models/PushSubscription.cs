using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PataoSmartQueuing.Models
{
    public class PushSubscription
    {
        [Key]
        public int SubscriptionID { get; set; }

        [Required]
        public int StudentID { get; set; }

        [Required]
        [MaxLength(500)]
        public string Endpoint { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string P256dh { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Auth { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        [ForeignKey(nameof(StudentID))]
        public virtual Student? Student { get; set; }
    }
}