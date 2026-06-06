using System;

namespace PataoSmartQueuing.Models
{
    public class AdminSettings
    {
        public int AdminSettingsID { get; set; }

        public string PortalToken { get; set; } = "SECRET123";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}