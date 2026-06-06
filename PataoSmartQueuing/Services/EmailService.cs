using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace PataoSmartQueuing.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            try
            {
                var smtpHost = _config["SmtpSettings:Host"];
                var smtpPortStr = _config["SmtpSettings:Port"];
                var smtpUser = _config["SmtpSettings:User"];
                var smtpPass = _config["SmtpSettings:Password"];
                var enableSslStr = _config["SmtpSettings:EnableSSL"];

                // LAYER 1: Try env var
                var fromEmail = _config["SmtpSettings:FromEmail"];

                // LAYER 2: If empty or invalid, hardcode
                if (string.IsNullOrWhiteSpace(fromEmail) || !fromEmail.Contains("@"))
                {
                    fromEmail = "garcianojohn21@gmail.com";
                }

                _logger.LogInformation($"DEBUG: FromEmail = '{fromEmail}'");
                _logger.LogInformation($"DEBUG: SmtpUser = '{smtpUser}'");

                if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser))
                {
                    _logger.LogError("SMTP settings are not configured properly");
                    throw new InvalidOperationException("SMTP settings missing in configuration");
                }

                var smtpPort = int.Parse(smtpPortStr ?? "587");
                var enableSsl = bool.Parse(enableSslStr ?? "true");

                using var smtpClient = new SmtpClient(smtpHost)
                {
                    Port = smtpPort,
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = enableSsl,
                    Timeout = 30000
                };

                // GUARANTEED valid email
                var fromAddress = new MailAddress(fromEmail, "Patao NHS Smart Queuing");

                using var mailMessage = new MailMessage
                {
                    From = fromAddress,
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true,
                    Priority = MailPriority.Normal
                };

                mailMessage.To.Add(toEmail);

                _logger.LogInformation($"Sending email to {toEmail} from {fromEmail}");
                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation($"✅ Email sent successfully to {toEmail}");
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError($"❌ SMTP Error: {smtpEx.Message}");
                throw new Exception($"Failed to send email: {smtpEx.Message}", smtpEx);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error sending email to {toEmail}: {ex.Message}");
                throw new Exception($"Failed to send email: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Send email with simple text wrapper (for backward compatibility)
        /// </summary>
        public async Task SendSimpleEmailAsync(string toEmail, string subject, string plainTextBody)
        {
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding:20px; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #28a745 0%, #20c997 100%); padding: 20px; border-radius: 10px 10px 0 0;'>
                        <h1 style='color: white; margin: 0; text-align: center;'>Patao National High School</h1>
                        <p style='color: white; margin: 5px 0 0 0; text-align: center; font-size: 14px;'>Smart Queuing System</p>
                    </div>
                    <div style='background: white; padding: 30px; border: 1px solid #e0e0e0; border-top: none;'>
                        <p style='color: #333; font-size: 16px; line-height: 1.6;'>{plainTextBody}</p>
                    </div>
                    <div style='background: #f8f9fa; padding: 15px; text-align: center; border-radius: 0 0 10px 10px;'>
                        <p style='color: #6c757d; font-size: 12px; margin: 0;'>
                            &copy; {DateTime.Now.Year} Patao National High School. All rights reserved.
                        </p>
                    </div>
                </div>
            ";

            await SendEmailAsync(toEmail, subject, htmlBody);
        }

        /// <summary>
        /// Test email configuration by sending a test email
        /// </summary>
        public async Task<bool> TestEmailConfigurationAsync(string testEmail)
        {
            try
            {
                await SendSimpleEmailAsync(
                    testEmail,
                    "Test Email - Patao NHS Queuing System",
                    "This is a test email to verify your SMTP configuration is working correctly."
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Email configuration test failed: {ex.Message}");
                return false;
            }
        }
    }
}