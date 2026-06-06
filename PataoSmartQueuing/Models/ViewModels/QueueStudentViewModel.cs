namespace PataoSmartQueuing.Models.ViewModels
{
    public class QueueStudentViewModel
    {
        public int QueueStudentID { get; set; }

        public string StudentName { get; set; } = string.Empty;

        public int QueueNumber { get; set; }

        public bool IsServing { get; set; }

        public bool IsDone { get; set; }

        public bool IsUnserved { get; set; }

        public string Email { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string ProfilePhoto { get; set; } = "/Images/ProfilePic.jfif";

        public string PinCode { get; set; } = string.Empty;
    }
}