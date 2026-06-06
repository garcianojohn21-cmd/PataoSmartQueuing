using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Hubs;

namespace PataoSmartQueuing.Services
{
    /// <summary>
    /// Background service that runs every 60 seconds and automatically closes
    /// any queue whose ScheduledEndTime has passed. Notifies all affected
    /// participants by email and broadcasts a SignalR refresh.
    /// Register in Program.cs:
    ///   builder.Services.AddHostedService&lt;QueueTimeoutService&gt;();
    /// </summary>
    public class QueueTimeoutService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QueueTimeoutService> _logger;

        public QueueTimeoutService(
            IServiceScopeFactory scopeFactory,
            ILogger<QueueTimeoutService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("QueueTimeoutService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndCloseExpiredQueues();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"QueueTimeoutService error: {ex.Message}");
                }

                // Check every 60 seconds
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }

            _logger.LogInformation("QueueTimeoutService stopped.");
        }

        private async Task CheckAndCloseExpiredQueues()
        {
            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

            // ALWAYS USE UTC FOR POSTGRESQL TIMESTAMPTZ
            var now = DateTime.UtcNow;

            _logger.LogInformation($"Queue timeout check running. UTC Time: {now:yyyy-MM-dd HH:mm:ss} ({now.Kind})");

            // Find active queues whose scheduled end time has passed
            var expiredQueues = await context.Queues
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .Where(q =>
                    q.IsActive &&
                    !q.IsDone &&
                    q.ScheduledEndTime.HasValue &&
                    q.ScheduledEndTime <= now)
                .ToListAsync();

            if (!expiredQueues.Any())
                return;

            foreach (var queue in expiredQueues)
            {
                _logger.LogInformation(
                    $"Auto-closing queue '{queue.QueueName}' (ID: {queue.QueueID})"
                );

                var affectedStudents = queue.QueueStudents
                    .Where(s =>
                        s.Status == "Pending" ||
                        s.Status == "Serving")
                    .ToList();

                foreach (var student in affectedStudents)
                {
                    student.Status = "Unserved";
                    student.IsUnserved = true;
                    student.IsServing = false;

                    try
                    {
                        await emailService.SendEmailAsync(
                            student.Student.Email,
                            "Queue Ended – Patao NHS Smart Queuing",
                            $@"
                    <div style='font-family: Arial, sans-serif; padding:20px; max-width:600px; margin:auto;'>
                        <h2 style='color:#dc3545;'>Queue Time Ended ⏰</h2>

                        <p>Hello <strong>{student.Student.FirstName}</strong>,</p>

                        <p>
                            The queue you joined has automatically ended because
                            the scheduled end time has been reached.
                        </p>

                        <div style='background:#f8f9fa;padding:15px;border-radius:8px;margin:20px 0;'>
                            <p><strong>Queue:</strong> {queue.QueueName}</p>
                            <p><strong>Queue Number:</strong> #{student.QueueNumber}</p>
                            <p><strong>Status:</strong> Unserved</p>
                            <p><strong>Ended At (UTC):</strong> {now:MMM dd, yyyy hh:mm tt}</p>
                        </div>

                        <p>
                            If you still need assistance, please contact the
                            registrar or join a new queue.
                        </p>

                        <hr />

                        <small style='color:#6c757d'>
                            Patao NHS Smart Queuing System
                        </small>
                    </div>"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"Failed sending email to {student.Student.Email}: {ex.Message}"
                        );
                    }
                }

                queue.IsDone = true;
                queue.IsActive = false;
                queue.Status = "Done";

                // UTC
                queue.DateCompleted = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();

            try
            {
                await hubContext.Clients.All.SendAsync("RefreshQueue");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"SignalR broadcast error: {ex.Message}"
                );
            }

            _logger.LogInformation(
                $"Successfully auto-closed {expiredQueues.Count} expired queue(s)."
            );
        }
    }
}