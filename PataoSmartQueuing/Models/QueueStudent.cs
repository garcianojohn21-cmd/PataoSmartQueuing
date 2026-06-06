using System;

namespace PataoSmartQueuing.Models
{
    public class QueueStudent
    {
        public int QueueStudentID { get; set; }

        public int QueueID { get; set; }

        public virtual Queue? Queue { get; set; }

        public int StudentID { get; set; }

        public virtual Student? Student { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public int QueueNumber { get; set; }

        public string PinCode { get; set; } = string.Empty;

        public bool IsServing { get; set; } = false;

        public bool IsDone { get; set; } = false;

        public bool IsUnserved { get; set; } = false;

        public string Status { get; set; } = "Pending";
    }
}