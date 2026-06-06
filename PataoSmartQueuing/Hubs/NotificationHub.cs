using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace PataoSmartQueuing.Hubs
{
    public class NotificationHub : Hub
    {
        // Store StudentID -> ConnectionID mapping
        private static ConcurrentDictionary<string, string> _connections = new();

        public override Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var studentId = httpContext?.Request.Query["studentId"].ToString();

            if (!string.IsNullOrEmpty(studentId))
            {
                _connections[studentId] = Context.ConnectionId;
                Console.WriteLine($"✅ Student {studentId} connected with ConnectionID {Context.ConnectionId}");
            }
            else
            {
                Console.WriteLine("⚠️ Connection made without studentId query parameter");
            }

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            var studentId = _connections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
            if (studentId != null)
            {
                _connections.TryRemove(studentId, out _);
                Console.WriteLine($"❌ Student {studentId} disconnected (ConnectionID {Context.ConnectionId})");
            }

            return base.OnDisconnectedAsync(exception);
        }

        // Helper method to send notification to one student
        public static async Task SendToStudent(IHubContext<NotificationHub> hub, string studentId, string title, string message, string type = "info")
        {
            if (_connections.TryGetValue(studentId, out var connId))
            {
                Console.WriteLine($"📢 Sending to Student {studentId}: {title} - {message}");
                await hub.Clients.Client(connId).SendAsync("ReceiveNotification", new
                {
                    title,
                    message,
                    type, // info, warning, success, error
                    timestamp = DateTime.Now
                });
            }
            else
            {
                Console.WriteLine($"⚠️ Student {studentId} is not connected. Notification skipped.");
            }
        }

        // Send to all connected clients
        public static async Task SendToAll(IHubContext<NotificationHub> hubContext, string title, string message, string type = "info")
        {
            await hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                title,
                message,
                type,
                timestamp = DateTime.Now
            });
        }

        // Refresh queue for all
        public static async Task RefreshQueue(IHubContext<NotificationHub> hubContext)
        {
            await hubContext.Clients.All.SendAsync("RefreshQueue");
        }
    }
}