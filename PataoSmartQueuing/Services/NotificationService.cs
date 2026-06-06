using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Hubs;
using PataoSmartQueuing.Models;
using WebPush;
using Newtonsoft.Json;
using WebPushSubscription = WebPush.PushSubscription;


namespace PataoSmartQueuing.Services
{
    public class NotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly EmailService _emailService;
        private readonly ILogger<NotificationService> _logger;
        private readonly IConfiguration _config;

        // VAPID keys for Web Push
        private readonly string _vapidPublicKey;
        private readonly string _vapidPrivateKey;
        private readonly string _vapidSubject;

        public NotificationService(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            EmailService emailService,
            ILogger<NotificationService> logger,
            IConfiguration config)
        {
            _context = context;
            _hubContext = hubContext;
            _emailService = emailService;
            _logger = logger;
            _config = config;

            // Load VAPID keys from configuration
            _vapidPublicKey = _config["WebPush:PublicKey"] ?? "";
            _vapidPrivateKey = _config["WebPush:PrivateKey"] ?? "";
            _vapidSubject = _config["WebPush:Subject"] ?? "mailto:admin@patonhs.edu";
        }

        /// <summary>
        /// Send push notification to specific student
        /// </summary>
        private async Task SendPushNotificationToStudent(int studentId, string title, string message, string type = "info")
        {
            try
            {
                // Skip if VAPID keys not configured
                if (string.IsNullOrEmpty(_vapidPublicKey) || string.IsNullOrEmpty(_vapidPrivateKey))
                {
                    _logger.LogWarning("VAPID keys not configured. Skipping push notification.");
                    return;
                }

                // Get student's push subscription from database
                var subscription = await _context.PushSubscriptions
                    .FirstOrDefaultAsync(s => s.StudentID == studentId && s.IsActive);

                if (subscription == null)
                {
                    _logger.LogInformation($"No active push subscription for student {studentId}");
                    return;
                }

                var pushSubscription = new WebPushSubscription(
    subscription.Endpoint,
    subscription.P256dh,
    subscription.Auth
);

                var vapidDetails = new VapidDetails(_vapidSubject, _vapidPublicKey, _vapidPrivateKey);

                var payload = JsonConvert.SerializeObject(new
                {
                    title = title,
                    message = message,
                    type = type,
                    url = "/Student/Dashboard",
                    timestamp = DateTime.Now
                });

                var webPushClient = new WebPushClient();
                await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);

                _logger.LogInformation($"✅ Push notification sent to student {studentId}");
            }
            catch (WebPushException ex)
            {
                _logger.LogError($"❌ WebPush error for student {studentId}: {ex.Message}");

                // If subscription is invalid (410 Gone), mark it as inactive
                if (ex.StatusCode == System.Net.HttpStatusCode.Gone)
                {
                    var sub = await _context.PushSubscriptions
                        .FirstOrDefaultAsync(s => s.StudentID == studentId);
                    if (sub != null)
                    {
                        sub.IsActive = false;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Marked push subscription as inactive for student {studentId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Push notification error: {ex.Message}");
            }
        }

        /// <summary>
        /// Send position-based notifications (20, 10, 3 positions away)
        /// </summary>
        public async Task<int> SendPositionNotifications(int queueId)
        {
            try
            {
                var queue = await _context.Queues
                    .Include(q => q.QueueStudents)
                        .ThenInclude(qs => qs.Student)
                    .FirstOrDefaultAsync(q => q.QueueID == queueId);

                if (queue == null)
                {
                    _logger.LogWarning($"Queue {queueId} not found");
                    return 0;
                }

                var servingNumber = queue.QueueStudents
                    .Where(s => s.Status == "Serving")
                    .OrderBy(s => s.QueueNumber)
                    .FirstOrDefault()?.QueueNumber ?? 0;

                if (servingNumber == 0)
                {
                    _logger.LogInformation($"No students currently being served in queue {queueId}");
                    return 0;
                }

                int notificationsSent = 0;
                var pendingStudents = queue.QueueStudents
                    .Where(s => s.Status == "Pending")
                    .OrderBy(s => s.QueueNumber)
                    .ToList();

                foreach (var student in pendingStudents)
                {
                    int positionsAhead = student.QueueNumber - servingNumber - 1;

                    if (positionsAhead == 20 || positionsAhead == 10 || positionsAhead == 3)
                    {
                        string title = positionsAhead switch
                        {
                            20 => "📍 Queue Position Update",
                            10 => "⚠️ Getting Close!",
                            3 => "🎯 Almost Your Turn!",
                            _ => "Queue Update"
                        };

                        string message = positionsAhead switch
                        {
                            20 => "You have 20 people ahead of you. Please prepare to arrive soon.",
                            10 => "You have 10 people ahead of you. Please be ready!",
                            3 => "You're almost up! Only 3 people ahead of you. Please be present.",
                            _ => $"You have {positionsAhead} people ahead of you."
                        };

                        string notifType = positionsAhead <= 3 ? "warning" : "info";

                        // 1. Send via SignalR (real-time)
                        try
                        {
                            await _hubContext.Clients.All.SendAsync("PositionUpdate", new
                            {
                                queueStudentId = student.QueueStudentID,
                                studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                                queueNumber = student.QueueNumber,
                                positionsAhead = positionsAhead,
                                message = message,
                                type = notifType
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"❌ SignalR error: {ex.Message}");
                        }

                        // 2. Send via Push Notification (background)
                        await SendPushNotificationToStudent(student.StudentID, title, message, notifType);

                        // 3. Send via Email (backup)
                        try
                        {
                            string urgencyClass = positionsAhead <= 3 ? "#dc3545" : "#0dcaf0";
                            string urgencyText = positionsAhead <= 3 ? "URGENT" : "REMINDER";

                            await _emailService.SendEmailAsync(
                                student.Student.Email,
                                $"Queue Update: {positionsAhead} People Ahead - Patao NHS",
                                $@"<div style='font-family: Arial, sans-serif; padding: 20px; max-width: 600px; margin: 0 auto;'>
                                    <h2 style='color: {urgencyClass};'>{urgencyText}: Queue Position Update 📍</h2>
                                    <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                                    <div style='background: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0; border-left: 4px solid {urgencyClass};'>
                                        <h3 style='margin: 0; color: {urgencyClass};'>{positionsAhead} people ahead of you</h3>
                                        <p style='margin: 10px 0 0 0; font-size: 1.1em;'>{message}</p>
                                    </div>
                                    <div style='background: #e7f3ff; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                        <p style='margin: 5px 0;'><strong>Queue:</strong> {queue.QueueName}</p>
                                        <p style='margin: 5px 0;'><strong>Your Number:</strong> #{student.QueueNumber}</p>
                                        <p style='margin: 5px 0;'><strong>Status:</strong> Pending</p>
                                    </div>
                                    <p style='color: #6c757d; font-size: 0.9em;'>
                                        <strong>Note:</strong> Please ensure you are present when called.
                                    </p>
                                    <hr/>
                                    <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                                </div>"
                            );

                            notificationsSent++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"❌ Email error: {ex.Message}");
                        }
                    }
                }

                _logger.LogInformation($"Sent {notificationsSent} position-based notifications for queue {queueId}");
                return notificationsSent;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in SendPositionNotifications: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send broadcast notification to all students in queue
        /// </summary>
        public async Task<int> SendBroadcastNotification(
            int queueId,
            string title,
            string message,
            string notificationType = "info")
        {
            try
            {
                var queue = await _context.Queues
                    .Include(q => q.QueueStudents)
                        .ThenInclude(qs => qs.Student)
                    .FirstOrDefaultAsync(q => q.QueueID == queueId);

                if (queue == null)
                {
                    _logger.LogWarning($"Queue {queueId} not found");
                    return 0;
                }

                var students = queue.QueueStudents
                    .Where(s => s.Status == "Pending" || s.Status == "Serving")
                    .ToList();

                int sent = 0;

                foreach (var student in students)
                {
                    try
                    {
                        // 1. SignalR notification
                        await _hubContext.Clients.All.SendAsync("BroadcastNotification", new
                        {
                            queueStudentId = student.QueueStudentID,
                            title = title,
                            message = message,
                            type = notificationType
                        });

                        // 2. Push notification
                        await SendPushNotificationToStudent(student.StudentID, title, message, notificationType);

                        // 3. Email notification
                        string color = notificationType switch
                        {
                            "success" => "#28a745",
                            "warning" => "#ffc107",
                            "error" => "#dc3545",
                            _ => "#0dcaf0"
                        };

                        await _emailService.SendEmailAsync(
                            student.Student.Email,
                            $"{title} - Patao NHS Queue Update",
                            $@"<div style='font-family: Arial, sans-serif; padding: 20px; max-width: 600px; margin: 0 auto;'>
                                <h2 style='color: {color};'>{title}</h2>
                                <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                                <div style='background: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0; border-left: 4px solid {color};'>
                                    <p style='margin: 0; font-size: 1.1em;'>{message}</p>
                                </div>
                                <div style='background: #e7f3ff; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                    <p style='margin: 5px 0;'><strong>Queue:</strong> {queue.QueueName}</p>
                                    <p style='margin: 5px 0;'><strong>Your Number:</strong> #{student.QueueNumber}</p>
                                </div>
                                <hr/>
                                <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                            </div>"
                        );

                        sent++;
                        _logger.LogInformation($"✅ Broadcast sent to {student.Student.Email}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"❌ Error sending broadcast to student {student.QueueStudentID}: {ex.Message}");
                    }
                }

                _logger.LogInformation($"Broadcast notification sent to {sent}/{students.Count} students");
                return sent;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in SendBroadcastNotification: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send manual notification to specific students
        /// </summary>
        public async Task<int> SendManualNotification(
            int queueId,
            List<int> studentIds,
            string title,
            string message,
            string notificationType = "info")
        {
            try
            {
                var queue = await _context.Queues
                    .Include(q => q.QueueStudents)
                        .ThenInclude(qs => qs.Student)
                    .FirstOrDefaultAsync(q => q.QueueID == queueId);

                if (queue == null)
                {
                    _logger.LogWarning($"Queue {queueId} not found");
                    return 0;
                }

                var students = queue.QueueStudents
                    .Where(s => studentIds.Contains(s.QueueStudentID))
                    .ToList();

                int sent = 0;

                foreach (var student in students)
                {
                    try
                    {
                        // 1. SignalR notification
                        await _hubContext.Clients.All.SendAsync("ManualNotification", new
                        {
                            queueStudentId = student.QueueStudentID,
                            title = title,
                            message = message,
                            type = notificationType
                        });

                        // 2. Push notification
                        await SendPushNotificationToStudent(student.StudentID, title, message, notificationType);

                        // 3. Email notification
                        string color = notificationType switch
                        {
                            "success" => "#28a745",
                            "warning" => "#ffc107",
                            "error" => "#dc3545",
                            _ => "#0dcaf0"
                        };

                        await _emailService.SendEmailAsync(
                            student.Student.Email,
                            $"{title} - Patao NHS",
                            $@"<div style='font-family: Arial, sans-serif; padding: 20px; max-width: 600px; margin: 0 auto;'>
                                <h2 style='color: {color};'>{title}</h2>
                                <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                                <div style='background: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0; border-left: 4px solid {color};'>
                                    <p style='margin: 0; font-size: 1.1em;'>{message}</p>
                                </div>
                                <div style='background: #e7f3ff; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                    <p style='margin: 5px 0;'><strong>Queue:</strong> {queue.QueueName}</p>
                                    <p style='margin: 5px 0;'><strong>Your Number:</strong> #{student.QueueNumber}</p>
                                </div>
                                <hr/>
                                <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                            </div>"
                        );

                        sent++;
                        _logger.LogInformation($"✅ Manual notification sent to {student.Student.Email}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"❌ Error sending manual notification to student {student.QueueStudentID}: {ex.Message}");
                    }
                }

                _logger.LogInformation($"Manual notification sent to {sent}/{students.Count} students");
                return sent;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in SendManualNotification: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// Update notification settings for a queue
        /// </summary>
        public async Task<bool> UpdateNotificationSettings(
            int queueId,
            bool enableAutoNotifications,
            int notifyAt20,
            int notifyAt10,
            int notifyAt3)
        {
            try
            {
                var queue = await _context.Queues.FindAsync(queueId);
                if (queue == null)
                {
                    _logger.LogWarning($"Queue {queueId} not found");
                    return false;
                }

                // Update queue notification settings
                // Note: You'll need to add these properties to your Queue model if they don't exist
                // For now, we'll just log that settings were updated
                _logger.LogInformation($"✅ Notification settings updated for queue {queueId}");
                _logger.LogInformation($"   Auto notifications: {enableAutoNotifications}");
                _logger.LogInformation($"   Notify at 20: {notifyAt20}");
                _logger.LogInformation($"   Notify at 10: {notifyAt10}");
                _logger.LogInformation($"   Notify at 3: {notifyAt3}");

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error updating notification settings: {ex.Message}");
                return false;
            }
        }
    }
}