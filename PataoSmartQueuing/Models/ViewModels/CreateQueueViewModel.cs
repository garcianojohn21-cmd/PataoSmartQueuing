using System;
using System.ComponentModel.DataAnnotations;

namespace PataoSmartQueuing.ViewModels
{
    public class CreateQueueViewModel
    {
        [Required(ErrorMessage = "Queue name is required.")]
        [MaxLength(100)]
        public string QueueName { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        [Range(1, 1000)]
        public int MaxStudents { get; set; }

        [Range(1, 10)]
        public int ServingBatchSize { get; set; } = 1;

        public DateTime? ScheduledEndTime { get; set; }

        public bool ActivateImmediately { get; set; } = false;
    }
}