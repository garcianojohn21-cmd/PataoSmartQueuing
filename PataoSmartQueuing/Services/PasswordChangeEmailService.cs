using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PataoSmartQueuing.Services;

namespace PataoSmartQueuing.Services
{
    public class PasswordChangeEmailService
    {
        private readonly EmailService _emailService;
        private readonly ILogger<PasswordChangeEmailService> _logger;

        public PasswordChangeEmailService(EmailService emailService, ILogger<PasswordChangeEmailService> logger)
        {
            _emailService = emailService;
            _logger = logger;
        }

        public async Task SendPasswordChangedNotificationAsync(string toEmail, string firstName, string lastName)
        {
            var fullName = $"{firstName} {lastName}";
            var changedAt = DateTime.Now.ToString("MMMM dd, yyyy hh:mm tt");

            var subject = "🔐 Password Changed - Patao NHS Smart Queuing";

            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>

                    <!-- Header -->
                    <div style='background: linear-gradient(135deg, #3DDC84 0%, #2AB366 100%);
                                padding: 30px 20px; border-radius: 12px 12px 0 0; text-align: center;'>
                        <h1 style='color: white; margin: 0; font-size: 24px;'>
                            🏫 Patao National High School
                        </h1>
                        <p style='color: rgba(255,255,255,0.9); margin: 6px 0 0 0; font-size: 14px;'>
                            Smart Queuing System
                        </p>
                    </div>

                    <!-- Body -->
                    <div style='background: #ffffff; padding: 35px 30px;
                                border: 1px solid #e0e0e0; border-top: none;'>

                        <h2 style='color: #1E8B57; margin-top: 0;'>Password Changed Successfully</h2>

                        <p style='color: #333; font-size: 15px; line-height: 1.6;'>
                            Hello, <strong>{fullName}</strong>!
                        </p>

                        <p style='color: #333; font-size: 15px; line-height: 1.6;'>
                            Your account password was successfully changed on:
                        </p>

                        <!-- Date/Time Box -->
                        <div style='background: #E8FFF3; border: 2px solid #3DDC84;
                                    border-radius: 10px; padding: 14px 20px; margin: 20px 0;
                                    text-align: center;'>
                            <span style='color: #1E8B57; font-size: 16px; font-weight: bold;'>
                                🕐 {changedAt}
                            </span>
                        </div>

                        <!-- Warning Box -->
                        <div style='background: #FEF9E7; border: 2px solid #F39C12;
                                    border-radius: 10px; padding: 16px 20px; margin: 20px 0;'>
                            <p style='color: #856404; font-size: 14px; margin: 0; line-height: 1.6;'>
                                ⚠️ <strong>Didn't make this change?</strong><br/>
                                If you did not request this password change, please contact your
                                school administrator immediately to secure your account.
                            </p>
                        </div>

                        <p style='color: #555; font-size: 14px; line-height: 1.6;'>
                            If you made this change yourself, no further action is needed.
                            Keep your new password safe and do not share it with anyone.
                        </p>
                    </div>

                    <!-- Footer -->
                    <div style='background: #f8f9fa; padding: 18px 20px;
                                text-align: center; border-radius: 0 0 12px 12px;
                                border: 1px solid #e0e0e0; border-top: none;'>
                        <p style='color: #6c757d; font-size: 12px; margin: 0;'>
                            &copy; {DateTime.Now.Year} Patao National High School. All rights reserved.<br/>
                            This is an automated message. Please do not reply to this email.
                        </p>
                    </div>

                </div>
            ";

            try
            {
                await _emailService.SendEmailAsync(toEmail, subject, htmlBody);
                _logger.LogInformation($"✅ Password change notification sent to {toEmail}");
            }
            catch (Exception ex)
            {
                // Log the error but don't throw — password was already changed successfully.
                // A failed notification email should NOT roll back the password update.
                _logger.LogError($"❌ Failed to send password change notification to {toEmail}: {ex.Message}");
            }
        }
    }
}
