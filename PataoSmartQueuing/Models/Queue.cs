using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.Models
{
    public class Queue
    {
        public int QueueID { get; set; }

        [Required]
        [MaxLength(100)]
        public string QueueName { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public int MaxStudents { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(50)]
        public string QueueCode { get; set; } = string.Empty;

        public int CreatedByAdminID { get; set; }

        public virtual Admin? CreatedByAdmin { get; set; }

        public virtual ICollection<QueueStudent> QueueStudents { get; set; }
            = new List<QueueStudent>();

        public bool IsActive { get; set; } = false;

        public bool IsDone { get; set; } = false;

        public DateTime? DateCompleted { get; set; }

        public string Status { get; set; } = "Pending";

        public int ServingBatchSize { get; set; } = 1;

        public bool EnableAutoNotifications { get; set; } = true;

        public int NotifyAt20Ahead { get; set; } = 20;

        public int NotifyAt10Ahead { get; set; } = 10;

        public int NotifyAt3Ahead { get; set; } = 3;

        public DateTime? ScheduledEndTime { get; set; }
    }
}