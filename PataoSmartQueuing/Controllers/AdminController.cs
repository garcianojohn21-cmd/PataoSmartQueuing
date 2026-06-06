using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Hubs;
using PataoSmartQueuing.Models;
using PataoSmartQueuing.Models.ViewModels;
using PataoSmartQueuing.Services;
using PataoSmartQueuing.ViewModels;
using System;
using System.Text;

namespace PataoSmartQueuing.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ApplicationDbContext context,
            IConfiguration config,
            EmailService emailService,
            IHubContext<NotificationHub> hubContext,
            ILogger<AdminController> logger)
        {
            _context = context;
            _config = config;
            _emailService = emailService;
            _hubContext = hubContext;
            _logger = logger;
        }

        // =====================
        // ROLE RESTRICTION HELPER
        // =====================
        private bool RequireRole(params string[] roles)
        {
            var role = HttpContext.Session.GetString("AdminRole");
            if (string.IsNullOrEmpty(role) || !roles.Contains(role))
                return false;
            return true;
        }

        // =====================
        // LOGIN
        // =====================
        [HttpGet]
        public async Task<IActionResult> Login()
        {
            if (HttpContext.Session.GetInt32("StudentID") != null)
                return RedirectToAction("Dashboard", "Student");

            var portalTokenFromDb = await _context.AdminSettings
                .Select(a => a.PortalToken)
                .FirstOrDefaultAsync();

            var tokenFromQuery = HttpContext.Request.Query["token"].ToString();

            if (string.IsNullOrEmpty(tokenFromQuery) || tokenFromQuery != portalTokenFromDb)
                return Unauthorized("Access denied. Invalid portal token.");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(AdminLoginViewModel model, string token)
        {
            var portalTokenFromDb = await _context.AdminSettings
                .Select(a => a.PortalToken)
                .FirstOrDefaultAsync();

            if (token != portalTokenFromDb)
                return Unauthorized("Invalid portal token.");

            if (!ModelState.IsValid) return View(model);

            var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Email == model.Email);
            if (admin == null || !BCrypt.Net.BCrypt.Verify(model.Password, admin.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid login attempt.");
                return View(model);
            }

            HttpContext.Session.SetInt32("AdminID", admin.AdminID);
            HttpContext.Session.SetString("AdminName", admin.FirstName);
            HttpContext.Session.SetString("AdminRole", admin.Role);

            return RedirectToAction("Dashboard");
        }

        // =====================
        // LOGOUT — reads token from DB so it stays correct after token changes
        // =====================
        public async Task<IActionResult> Logout()
        {
            var token = await _context.AdminSettings
                .Select(a => a.PortalToken)
                .FirstOrDefaultAsync();

            HttpContext.Session.Clear();
            return RedirectToAction("Login", new { token });
        }

        // =====================
        // DASHBOARD
        // =====================
        public IActionResult Dashboard()
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Staff", "Advance"))
                return RedirectToAction("Login", new { token = _config["AdminSettings:PortalToken"] });

            return View();
        }

        // =====================
        // DASHBOARD - OVERALL STATISTICS API
        // Called by Dashboard.cshtml via fetch('/Admin/GetOverallStatistics')
        // =====================
        [HttpGet]
        public async Task<JsonResult> GetOverallStatistics()
        {
            try
            {
                var allQueues = await _context.Queues
                    .Include(q => q.QueueStudents)
                    .OrderByDescending(q => q.DateCreated)
                    .ToListAsync();

                var allStudents = allQueues.SelectMany(q => q.QueueStudents).ToList();

                // Overview totals
                int totalQueues = allQueues.Count;
                int totalStudentsServed = allStudents.Count(s => s.Status == "Done");
                int totalStudentsInQueue = allStudents.Count(s => s.Status == "Pending" || s.Status == "Serving");
                int totalUnserved = allStudents.Count(s => s.Status == "Unserved");
                int totalServing = allStudents.Count(s => s.Status == "Serving");

                // Recent queues with per-queue student counts (for bar chart)
                var recentQueues = allQueues
                    .Take(20)
                    .Select(q => new {
                        queueId = q.QueueID,
                        queueName = q.QueueName,
                        dateCreated = q.DateCreated,
                        status = q.Status,
                        isActive = q.IsActive,
                        isDone = q.IsDone,
                        studentCount = q.QueueStudents?.Count ?? 0,
                        doneCount = q.QueueStudents?.Count(s => s.Status == "Done") ?? 0,
                        pendingCount = q.QueueStudents?.Count(s => s.Status == "Pending") ?? 0
                    }).ToList();

                // Daily data for last 30 days (for area chart)
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);
                var dailyData = allQueues
                    .Where(q => q.DateCreated >= thirtyDaysAgo)
                    .GroupBy(q => q.DateCreated.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new {
                        date = g.Key.ToString("MMM dd"),
                        queues = g.Count(),
                        completed = g.SelectMany(q => q.QueueStudents).Count(s => s.Status == "Done"),
                        pending = g.SelectMany(q => q.QueueStudents).Count(s => s.Status == "Pending"),
                        unserved = g.SelectMany(q => q.QueueStudents).Count(s => s.Status == "Unserved")
                    }).ToList();

                // Previous 7-day vs current 7-day for delta indicators
                var now = DateTime.Now;
                var thisWeekStart = now.Date.AddDays(-7);
                var prevWeekStart = now.Date.AddDays(-14);

                var thisWeekQueues = allQueues.Where(q => q.DateCreated >= thisWeekStart).ToList();
                var prevWeekQueues = allQueues.Where(q => q.DateCreated >= prevWeekStart && q.DateCreated < thisWeekStart).ToList();

                int thisWeekServed = thisWeekQueues.SelectMany(q => q.QueueStudents).Count(s => s.Status == "Done");
                int prevWeekServed = prevWeekQueues.SelectMany(q => q.QueueStudents).Count(s => s.Status == "Done");
                int thisWeekTotal = thisWeekQueues.SelectMany(q => q.QueueStudents).Count();
                int prevWeekTotal = prevWeekQueues.SelectMany(q => q.QueueStudents).Count();
                double thisCompletion = thisWeekTotal > 0 ? Math.Round((thisWeekServed * 100.0) / thisWeekTotal, 1) : 0;
                double prevCompletion = prevWeekTotal > 0 ? Math.Round((prevWeekServed * 100.0) / prevWeekTotal, 1) : 0;
                double thisAvg = thisWeekQueues.Count > 0 ? Math.Round((double)thisWeekTotal / thisWeekQueues.Count, 1) : 0;
                double prevAvg = prevWeekQueues.Count > 0 ? Math.Round((double)prevWeekTotal / prevWeekQueues.Count, 1) : 0;

                return Json(new
                {
                    success = true,
                    overview = new
                    {
                        totalQueues,
                        totalStudentsServed,
                        totalStudentsInQueue,
                        totalUnserved,
                        totalServing,
                        activeQueues = allQueues.Count(q => q.IsActive),
                        doneQueues = allQueues.Count(q => q.IsDone)
                    },
                    deltas = new
                    {
                        queues = thisWeekQueues.Count - prevWeekQueues.Count,
                        served = thisWeekServed - prevWeekServed,
                        completion = Math.Round(thisCompletion - prevCompletion, 1),
                        avg = Math.Round(thisAvg - prevAvg, 1)
                    },
                    statusDist = new
                    {
                        done = totalStudentsServed,
                        serving = totalServing,
                        pending = totalStudentsInQueue - totalServing,
                        unserved = totalUnserved
                    },
                    recentQueues,
                    dailyData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetOverallStatistics error: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =====================
        // MANAGE QUEUES
        // =====================
        public async Task<IActionResult> ManageQueues()
        {
            var queues = await _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                .OrderByDescending(q => q.DateCreated)
                .ToListAsync();

            return View(queues);
        }

        // =====================
        // CREATE QUEUE
        // =====================
        [HttpGet]
        public IActionResult CreateQueue()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQueue(CreateQueueViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    success = false,
                    errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });
            }

            if (await _context.Queues.AnyAsync(q => q.QueueName == model.QueueName))
            {
                return Json(new
                {
                    success = false,
                    errors = new[] { "Queue name already exists. Please choose another one." }
                });
            }

            if (model.ScheduledEndTime.HasValue && model.ScheduledEndTime.Value <= DateTime.Now)
            {
                return Json(new
                {
                    success = false,
                    errors = new[] { "Scheduled end time must be in the future." }
                });
            }

            var uniqueCode = GenerateUniqueQueueCode();

            if (model.ActivateImmediately)
            {
                var allActiveQueues = await _context.Queues.Where(q => q.IsActive).ToListAsync();
                foreach (var activeQueue in allActiveQueues)
                {
                    activeQueue.IsActive = false;
                    activeQueue.Status = "Pending";
                }
            }

            var queue = new Queue
            {
                QueueName = model.QueueName,
                Description = model.Description,
                MaxStudents = model.MaxStudents,
                ServingBatchSize = model.ServingBatchSize,
                QueueCode = uniqueCode,
                DateCreated = DateTime.Now,
                CreatedByAdminID = HttpContext.Session.GetInt32("AdminID") ?? 1,
                IsActive = model.ActivateImmediately,
                IsDone = false,
                Status = model.ActivateImmediately ? "Active" : "Pending",
                ScheduledEndTime = model.ScheduledEndTime
            };

            _context.Queues.Add(queue);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                queueName = queue.QueueName,
                queueCode = uniqueCode,
                isActive = queue.IsActive,
                status = queue.Status,
                joinLink = $"{Request.Scheme}://{Request.Host}/Student/JoinQueue?code={uniqueCode}"
            });
        }

        // =====================
        // USE QUEUE (set as Active)
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UseQueueAjax(int id)
        {
            var queue = await _context.Queues.FindAsync(id);
            if (queue == null)
                return Json(new { success = false, message = "Queue not found." });

            if (queue.IsDone)
                return Json(new { success = false, message = "Cannot activate a completed queue. Please restore it first." });

            if (queue.IsActive)
                return Json(new { success = false, message = "This queue is already active." });

            var allActiveQueues = await _context.Queues
                .Where(q => q.IsActive && q.QueueID != id)
                .ToListAsync();

            foreach (var activeQueue in allActiveQueues)
            {
                activeQueue.IsActive = false;
                activeQueue.Status = "Pending";
            }

            queue.IsActive = true;
            queue.IsDone = false;
            queue.Status = "Active";

            await _context.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("QueueActivated", new
                {
                    queueId = queue.QueueID,
                    queueName = queue.QueueName
                });
                await _hubContext.Clients.All.SendAsync("RefreshQueue");
            }
            catch (Exception ex)
            {
                _logger.LogError($"SignalR error: {ex.Message}");
            }

            return Json(new
            {
                success = true,
                message = $"Queue '{queue.QueueName}' is now active.",
                queueId = queue.QueueID,
                isActive = queue.IsActive,
                isDone = queue.IsDone,
                statusText = "Active"
            });
        }

        // =====================
        // MARK QUEUE AS DONE
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> DoneQueueAjax(int id)
        {
            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
                return Json(new { success = false, message = "Queue not found." });

            if (!queue.IsActive)
                return Json(new { success = false, message = "Only active queues can be marked as done." });

            var servingCount = queue.QueueStudents?.Count(s => s.Status == "Serving") ?? 0;
            if (servingCount > 0)
            {
                return Json(new
                {
                    success = false,
                    message = $"Cannot mark queue as done. {servingCount} student(s) are still being served. Please complete their service first."
                });
            }

            var pendingStudents = queue.QueueStudents?
                .Where(s => s.Status == "Pending")
                .ToList();

            int notifiedCount = 0;

            if (pendingStudents != null && pendingStudents.Any())
            {
                foreach (var student in pendingStudents)
                {
                    student.Status = "Unserved";
                    student.IsUnserved = true;

                    try
                    {
                        await _emailService.SendEmailAsync(
                            student.Student.Email,
                            "Queue Cancelled – Patao NHS Smart Queuing",
                            $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                                <h2 style='color: #dc3545;'>Queue Cancelled ⚠️</h2>
                                <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                                <p>We regret to inform you that the queue you joined has been <strong>cancelled</strong> by the administrator.</p>
                                <div style='background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                    <p style='margin: 5px 0;'><strong>Queue:</strong> {queue.QueueName}</p>
                                    <p style='margin: 5px 0;'><strong>Your Queue Number:</strong> #{student.QueueNumber}</p>
                                    <p style='margin: 5px 0;'><strong>Status:</strong> <span style='color:#dc3545;'>Cancelled / Unserved</span></p>
                                </div>
                                <p>Please visit the registrar or rejoin a new queue if you still need assistance.</p>
                                <hr/>
                                <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                            </div>"
                        );
                        notifiedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Email error for {student.Student.Email}: {ex.Message}");
                    }
                }
            }

            queue.IsDone = true;
            queue.IsActive = false;
            queue.Status = "Done";
            queue.DateCompleted = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("QueueCompleted", new
                {
                    queueId = queue.QueueID,
                    queueName = queue.QueueName
                });
                await _hubContext.Clients.All.SendAsync("RefreshQueue");
            }
            catch { }

            return Json(new
            {
                success = true,
                message = $"Queue '{queue.QueueName}' marked as done. {notifiedCount} participant(s) notified by email.",
                queueId = queue.QueueID,
                isActive = false,
                isDone = true,
                statusText = "Done"
            });
        }

        // =====================
        // ARCHIVE QUEUE
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ArchiveQueueAjax(int id)
        {
            try
            {
                var queue = await _context.Queues
                    .Include(q => q.QueueStudents)
                        .ThenInclude(qs => qs.Student)
                    .FirstOrDefaultAsync(q => q.QueueID == id);

                if (queue == null)
                    return Json(new { success = false, message = "Queue not found." });

                if (queue.IsActive)
                    return Json(new { success = false, message = "Cannot archive an active queue. Please mark it as done first." });

                var activeStudents = queue.QueueStudents?
                    .Where(s => s.Status == "Pending" || s.Status == "Serving")
                    .ToList();

                int notifiedCount = 0;

                if (activeStudents != null && activeStudents.Any())
                {
                    foreach (var student in activeStudents)
                    {
                        student.Status = "Unserved";
                        student.IsUnserved = true;
                        student.IsServing = false;

                        try
                        {
                            await _emailService.SendEmailAsync(
                                student.Student.Email,
                                "Queue Archived – Patao NHS Smart Queuing",
                                $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                                    <h2 style='color: #856404;'>Queue Archived 📦</h2>
                                    <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                                    <p>The queue you were in has been <strong>archived</strong> by the administrator.</p>
                                    <div style='background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                        <p style='margin: 5px 0;'><strong>Queue:</strong> {queue.QueueName}</p>
                                        <p style='margin: 5px 0;'><strong>Your Queue Number:</strong> #{student.QueueNumber}</p>
                                        <p style='margin: 5px 0;'><strong>Status:</strong> <span style='color:#856404;'>Unserved</span></p>
                                    </div>
                                    <p>Please contact the registrar or rejoin a new queue if you still need assistance.</p>
                                    <hr/>
                                    <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                                </div>"
                            );
                            notifiedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Email error for {student.Student.Email}: {ex.Message}");
                        }
                    }
                }

                queue.IsDone = true;
                queue.IsActive = false;
                queue.Status = "Done";
                queue.DateCompleted = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Queue '{queue.QueueName}' archived. {notifiedCount} participant(s) notified.",
                    queueId = queue.QueueID
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error archiving queue: {ex.Message}");
                return Json(new { success = false, message = "Error archiving queue: " + ex.Message });
            }
        }

        // =====================
        // RESTORE QUEUE
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> RestoreQueueAjax(int id)
        {
            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
                return Json(new { success = false, message = "Queue not found." });

            if (!queue.IsDone)
                return Json(new { success = false, message = "Only completed queues can be restored." });

            queue.IsDone = false;
            queue.IsActive = false;
            queue.Status = "Pending";
            queue.DateCompleted = null;

            var unservedStudents = queue.QueueStudents?.Where(s => s.Status == "Unserved").ToList();
            if (unservedStudents != null && unservedStudents.Any())
            {
                foreach (var student in unservedStudents)
                {
                    student.Status = "Pending";
                    student.IsUnserved = false;
                }
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Queue '{queue.QueueName}' restored successfully.",
                queueId = queue.QueueID,
                isActive = false,
                isDone = false,
                statusText = "Pending"
            });
        }

        // =====================
        // EDIT QUEUE
        // =====================
        [HttpGet]
        public async Task<IActionResult> EditQueue(int id)
        {
            var queue = await _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
                return NotFound();

            return View(queue);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQueue(Queue queue)
        {
            if (!ModelState.IsValid)
                return View(queue);

            var existingQueue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == queue.QueueID);

            if (existingQueue == null)
                return NotFound();

            if (existingQueue.QueueName != queue.QueueName)
            {
                var duplicateName = await _context.Queues
                    .AnyAsync(q => q.QueueName == queue.QueueName && q.QueueID != queue.QueueID);

                if (duplicateName)
                {
                    ModelState.AddModelError("QueueName", "A queue with this name already exists.");
                    return View(queue);
                }
            }

            var currentStudentCount = existingQueue.QueueStudents?.Count(s => s.Status != "Done") ?? 0;
            if (queue.MaxStudents < currentStudentCount)
            {
                ModelState.AddModelError("MaxStudents",
                    $"Cannot set maximum below current student count ({currentStudentCount}).");
                return View(queue);
            }

            if (queue.ServingBatchSize < 1 || queue.ServingBatchSize > 10)
            {
                ModelState.AddModelError("ServingBatchSize",
                    "Serving batch size must be between 1 and 10.");
                return View(queue);
            }

            if (queue.ScheduledEndTime.HasValue && queue.ScheduledEndTime.Value <= DateTime.Now)
            {
                ModelState.AddModelError("ScheduledEndTime",
                    "Scheduled end time must be in the future.");
                return View(queue);
            }

            existingQueue.QueueName = queue.QueueName;
            existingQueue.Description = queue.Description;
            existingQueue.MaxStudents = queue.MaxStudents;
            existingQueue.ServingBatchSize = queue.ServingBatchSize;
            existingQueue.ScheduledEndTime = queue.ScheduledEndTime;

            try
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Queue '{queue.QueueName}' updated successfully.";
                return RedirectToAction("ManageQueues");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while saving: " + ex.Message);
                return View(queue);
            }
        }

        // =====================
        // GET QUEUE STATUS
        // =====================
        [HttpGet]
        public async Task<JsonResult> GetQueueStatus(int id)
        {
            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
                return Json(new { success = false, message = "Queue not found." });

            var studentStats = new
            {
                total = queue.QueueStudents?.Count() ?? 0,
                pending = queue.QueueStudents?.Count(s => s.Status == "Pending") ?? 0,
                serving = queue.QueueStudents?.Count(s => s.Status == "Serving") ?? 0,
                done = queue.QueueStudents?.Count(s => s.Status == "Done") ?? 0,
                unserved = queue.QueueStudents?.Count(s => s.Status == "Unserved") ?? 0
            };

            return Json(new
            {
                success = true,
                queueId = queue.QueueID,
                queueName = queue.QueueName,
                isActive = queue.IsActive,
                isDone = queue.IsDone,
                status = queue.Status,
                maxStudents = queue.MaxStudents,
                servingBatchSize = queue.ServingBatchSize,
                scheduledEndTime = queue.ScheduledEndTime?.ToString("MMM dd, yyyy hh:mm tt"),
                students = studentStats
            });
        }

        // =====================
        // STUDENT IN QUEUE (ADMIN VIEW)
        // =====================
        [HttpGet]
        public async Task<IActionResult> StudentInQueue(int? queueId)
        {
            try
            {
                if (queueId == null || queueId == 0)
                {
                    var queueWithServing = await _context.QueueStudents
                        .Where(qs => qs.Status == "Serving")
                        .Select(qs => qs.QueueID)
                        .FirstOrDefaultAsync();

                    if (queueWithServing != 0)
                        queueId = queueWithServing;
                    else
                    {
                        var queueWithPending = await _context.QueueStudents
                            .Where(qs => qs.Status == "Pending")
                            .Select(qs => qs.QueueID)
                            .FirstOrDefaultAsync();

                        if (queueWithPending != 0)
                            queueId = queueWithPending;
                        else
                        {
                            var activeQueue = await _context.Queues
                                .Where(q => q.IsActive && !q.IsDone)
                                .OrderByDescending(q => q.DateCreated)
                                .FirstOrDefaultAsync();

                            if (activeQueue != null)
                                queueId = activeQueue.QueueID;
                        }
                    }
                }

                if (queueId == 0 || queueId == null)
                {
                    ViewBag.QueueId = 0;
                    ViewBag.QueueName = "No Active Queue";
                    TempData["Warning"] = "No active queue found. Please activate a queue from Manage Queues.";
                    return View(new List<QueueStudentViewModel>());
                }

                var queue = await _context.Queues
                    .AsNoTracking()
                    .FirstOrDefaultAsync(q => q.QueueID == queueId);

                if (queue == null)
                {
                    ViewBag.QueueId = 0;
                    ViewBag.QueueName = "Queue Not Found";
                    TempData["Error"] = $"Queue with ID {queueId} not found.";
                    return View(new List<QueueStudentViewModel>());
                }

                var students = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .Include(qs => qs.Queue)
                    .Where(qs => qs.QueueID == queueId && qs.Status != "Done")
                    .OrderBy(qs => qs.QueueNumber)
                    .ToListAsync();

                var viewModel = students.Select(qs => new QueueStudentViewModel
                {
                    QueueStudentID = qs.QueueStudentID,
                    StudentName = qs.Student != null
                        ? (qs.Student.FirstName + " " +
                           (string.IsNullOrEmpty(qs.Student.MiddleName) ? "" : qs.Student.MiddleName + " ") +
                           qs.Student.LastName).Trim()
                        : $"Unknown Student (ID: {qs.StudentID})",
                    QueueNumber = qs.QueueNumber,
                    Email = qs.Student?.Email ?? "N/A",
                    Status = qs.Status,
                    PinCode = qs.PinCode,
                    IsServing = qs.IsServing,
                    IsDone = qs.IsDone,
                    IsUnserved = qs.IsUnserved,
                    ProfilePhoto = qs.Student != null && !string.IsNullOrEmpty(qs.Student.ProfilePhoto)
                        ? qs.Student.ProfilePhoto
                        : "/Images/ProfilePic.jfif"
                }).ToList();

                ViewBag.QueueId = queueId;
                ViewBag.QueueName = queue.QueueName;
                ViewBag.QueueCode = queue.QueueCode;

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                ViewBag.JoinUrl = $"{baseUrl}/Student/JoinQueue?code={queue.QueueCode}";

                if (queue.ScheduledEndTime.HasValue)
                    ViewBag.ScheduledEndTime = queue.ScheduledEndTime.Value.ToString("MMM dd, yyyy hh:mm tt");

                if (!queue.IsActive)
                    TempData["Warning"] = $"Queue '{queue.QueueName}' is not currently active.";
                else if (!viewModel.Any())
                    TempData["Info"] = $"Queue '{queue.QueueName}' is active and ready for students! Share code: {queue.QueueCode}";

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception in StudentInQueue: {ex.Message}");
                TempData["Error"] = $"Error loading queue: {ex.Message}";
                ViewBag.QueueId = 0;
                ViewBag.QueueName = "Error";
                return View(new List<QueueStudentViewModel>());
            }
        }

        // =====================
        // SERVE STUDENT
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ServeStudent(int id)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .Include(qs => qs.Queue)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status != "Pending")
                    return Json(new { success = false, message = "This student is not in pending status." });

                var queue = student.Queue;
                var currentServingCount = await _context.QueueStudents
                    .CountAsync(s => s.QueueID == queue.QueueID && s.Status == "Serving");

                if (currentServingCount >= queue.ServingBatchSize)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Maximum serving limit ({queue.ServingBatchSize}) reached. Please complete current students first."
                    });
                }

                student.Status = "Serving";
                student.IsServing = true;
                student.IsDone = false;
                student.IsUnserved = false;

                await _context.SaveChangesAsync();

                try
                {
                    await _hubContext.Clients.All.SendAsync("StudentServing", new
                    {
                        queueStudentId = student.QueueStudentID,
                        studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                        queueNumber = student.QueueNumber,
                        status = "Serving"
                    });

                    await _hubContext.Clients.All.SendAsync("RefreshQueue");

                    var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                    await notificationService.SendPositionNotifications(queue.QueueID);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SignalR/Notification error: {ex.Message}");
                }

                return Json(new
                {
                    success = true,
                    message = $"Now serving {student.Student.FirstName} {student.Student.LastName}",
                    queueNumber = student.QueueNumber,
                    studentId = student.QueueStudentID,
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // MARK DONE WITH PIN
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkDoneWithPin(int id, string pinCode)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .Include(qs => qs.Queue)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status != "Serving")
                    return Json(new { success = false, message = "Student must be in 'Serving' status first." });

                if (string.IsNullOrWhiteSpace(pinCode))
                    return Json(new { success = false, message = "PIN code is required." });

                if (pinCode.Length != 6)
                    return Json(new { success = false, message = "PIN code must be 6 digits." });

                if (pinCode != student.PinCode)
                    return Json(new { success = false, message = "Incorrect PIN code." });

                student.Status = "Done";
                student.IsDone = true;
                student.IsServing = false;
                student.IsUnserved = false;

                await _context.SaveChangesAsync();

                try
                {
                    await _hubContext.Clients.All.SendAsync("StudentCompleted", new
                    {
                        queueStudentId = student.QueueStudentID,
                        studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                        queueNumber = student.QueueNumber,
                        status = "Done"
                    });

                    await _hubContext.Clients.All.SendAsync("RefreshQueue");

                    var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                    await notificationService.SendPositionNotifications(student.Queue.QueueID);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SignalR/Notification error: {ex.Message}");
                }

                try
                {
                    await _emailService.SendEmailAsync(
                        student.Student.Email,
                        "Service Completed – Patao NHS Smart Queuing",
                        $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                            <h2 style='color: #28a745;'>Service Completed! ✅</h2>
                            <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                            <p>Your queue service has been completed successfully.</p>
                            <div style='background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                <p style='margin: 5px 0;'><strong>Queue:</strong> {student.Queue.QueueName}</p>
                                <p style='margin: 5px 0;'><strong>Queue Number:</strong> #{student.QueueNumber}</p>
                                <p style='margin: 5px 0;'><strong>Status:</strong> <span style='color: #28a745;'>Completed</span></p>
                            </div>
                            <p>Thank you for using our smart queuing system!</p>
                            <hr/>
                            <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                        </div>"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Email error: {ex.Message}");
                }

                return Json(new
                {
                    success = true,
                    message = $"{student.Student.FirstName} {student.Student.LastName} marked as Done!",
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                    queueNumber = student.QueueNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // VALIDATE PIN ONLY
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidatePinOnly(int id, string pinCode)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status != "Serving")
                    return Json(new { success = false, message = "Student must be in 'Serving' status." });

                if (string.IsNullOrWhiteSpace(pinCode))
                    return Json(new { success = false, message = "PIN code is required." });

                if (pinCode.Length != 6)
                    return Json(new { success = false, message = "PIN code must be 6 digits." });

                if (pinCode != student.PinCode)
                    return Json(new { success = false, message = "Incorrect PIN code." });

                return Json(new
                {
                    success = true,
                    message = "PIN verified successfully!",
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                    queueNumber = student.QueueNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error validating PIN: " + ex.Message });
            }
        }

        // =====================
        // UNSERVE STUDENT
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnserveStudent(int id)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status == "Done")
                    return Json(new { success = false, message = "Cannot unserve a completed student." });

                if (student.Status == "Unserved")
                    return Json(new { success = false, message = "Student is already marked as unserved." });

                student.Status = "Unserved";
                student.IsUnserved = true;
                student.IsServing = false;
                student.IsDone = false;

                await _context.SaveChangesAsync();

                try
                {
                    await _hubContext.Clients.All.SendAsync("StudentUnserved", new
                    {
                        queueStudentId = student.QueueStudentID,
                        studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                        queueNumber = student.QueueNumber,
                        status = "Unserved"
                    });

                    await _hubContext.Clients.All.SendAsync("RefreshQueue");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SignalR error: {ex.Message}");
                }

                try
                {
                    await _emailService.SendEmailAsync(
                        student.Student.Email,
                        "Queue Update: Unserved – Patao NHS Smart Queuing",
                        $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                            <h2 style='color: #dc3545;'>Queue Update ⚠️</h2>
                            <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                            <p>You were marked as <strong>Unserved</strong> for Queue #{student.QueueNumber}.</p>
                            <p>Please rejoin the queue if you still need service.</p>
                            <hr/>
                            <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                        </div>"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Email error: {ex.Message}");
                }

                return Json(new
                {
                    success = true,
                    message = $"{student.Student.FirstName} {student.Student.LastName} marked as Unserved.",
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // DONE STUDENT LIST
        // =====================
        public async Task<IActionResult> DoneStudent(int? queueId)
        {
            if (queueId == null)
            {
                queueId = await _context.Queues
                    .Where(q => q.IsActive && !q.IsDone)
                    .Select(q => q.QueueID)
                    .FirstOrDefaultAsync();
            }

            if (queueId == 0 || queueId == null)
            {
                ViewBag.QueueId = 0;
                ViewBag.QueueName = "No Active Queue";
                TempData["Info"] = "No active queue found.";
                return View(new List<QueueStudent>());
            }

            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .FirstOrDefaultAsync(q => q.QueueID == queueId);

            if (queue == null)
            {
                TempData["Error"] = "Queue not found.";
                return RedirectToAction("ManageQueues");
            }

            var doneStudents = queue.QueueStudents
                .Where(qs => qs.Status == "Done" && qs.IsDone)
                .OrderBy(qs => qs.QueueNumber)
                .ToList();

            ViewBag.QueueId = queue.QueueID;
            ViewBag.QueueName = queue.QueueName;
            ViewBag.TotalDone = doneStudents.Count;

            return View(doneStudents);
        }

        // =====================
        // UNSERVED LIST
        // =====================
        [HttpGet]
        public async Task<IActionResult> UnservedList(int? queueId)
        {
            var queues = await _context.Queues
                .OrderByDescending(q => q.DateCreated)
                .Select(q => new { q.QueueID, q.QueueName, q.IsActive, q.IsDone })
                .ToListAsync();

            ViewBag.Queues = queues;

            if (queueId == null)
            {
                queueId = await _context.QueueStudents
                    .Where(qs => qs.Status == "Unserved")
                    .Select(qs => qs.QueueID)
                    .FirstOrDefaultAsync();
            }

            if (queueId == 0 || queueId == null)
            {
                ViewBag.QueueId = 0;
                ViewBag.QueueName = "No Queue Selected";
                return View(new List<QueueStudentViewModel>());
            }

            var selectedQueue = await _context.Queues.FirstOrDefaultAsync(q => q.QueueID == queueId);

            if (selectedQueue == null)
            {
                TempData["Error"] = "Queue not found.";
                return RedirectToAction("ManageQueues");
            }

            var unservedStudents = await _context.QueueStudents
                .Include(qs => qs.Student)
                .Include(qs => qs.Queue)
                .Where(qs => qs.QueueID == queueId && qs.Status == "Unserved")
                .OrderBy(qs => qs.QueueNumber)
                .Select(qs => new QueueStudentViewModel
                {
                    QueueStudentID = qs.QueueStudentID,
                    StudentName = (qs.Student.FirstName + " " +
                                     (qs.Student.MiddleName ?? "") + " " +
                                     qs.Student.LastName).Trim(),
                    QueueNumber = qs.QueueNumber,
                    Email = qs.Student.Email,
                    Status = qs.Status,
                    PinCode = qs.PinCode,
                    ProfilePhoto = string.IsNullOrEmpty(qs.Student.ProfilePhoto)
                        ? "/Images/ProfilePic.jfif"
                        : qs.Student.ProfilePhoto
                })
                .ToListAsync();

            ViewBag.QueueId = queueId;
            ViewBag.QueueName = selectedQueue.QueueName;
            ViewBag.TotalUnserved = unservedStudents.Count;

            return View(unservedStudents);
        }

        // =====================
        // VALIDATE UNSERVED PIN
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidateUnservedPin(int id, string pinCode)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status != "Unserved")
                    return Json(new { success = false, message = "Student must be in 'Unserved' status." });

                if (string.IsNullOrWhiteSpace(pinCode) || pinCode.Length != 6)
                    return Json(new { success = false, message = "PIN code must be 6 characters." });

                if (pinCode != student.PinCode)
                    return Json(new { success = false, message = "Incorrect PIN code." });

                return Json(new
                {
                    success = true,
                    message = "PIN verified successfully!",
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                    queueNumber = student.QueueNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // COMPLETE UNSERVED STUDENT
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteUnservedStudent(int id, string pinCode)
        {
            try
            {
                var student = await _context.QueueStudents
                    .Include(qs => qs.Student)
                    .Include(qs => qs.Queue)
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == id);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                if (student.Status != "Unserved")
                    return Json(new { success = false, message = "Student must be in 'Unserved' status." });

                if (string.IsNullOrWhiteSpace(pinCode) || pinCode.Length != 6)
                    return Json(new { success = false, message = "Invalid PIN code." });

                if (pinCode != student.PinCode)
                    return Json(new { success = false, message = "Incorrect PIN code." });

                student.Status = "Done";
                student.IsDone = true;
                student.IsServing = false;
                student.IsUnserved = false;

                await _context.SaveChangesAsync();

                try
                {
                    await _hubContext.Clients.All.SendAsync("StudentCompleted", new
                    {
                        queueStudentId = student.QueueStudentID,
                        studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                        queueNumber = student.QueueNumber,
                        status = "Done"
                    });
                    await _hubContext.Clients.All.SendAsync("RefreshQueue");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SignalR error: {ex.Message}");
                }

                try
                {
                    await _emailService.SendEmailAsync(
                        student.Student.Email,
                        "Service Completed (Manually) – Patao NHS Smart Queuing",
                        $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                            <h2 style='color: #28a745;'>Service Completed! ✅</h2>
                            <p>Hello <strong>{student.Student.FirstName}</strong>,</p>
                            <p>Your queue service has been <strong>manually completed</strong> by our staff.</p>
                            <div style='background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                <p style='margin: 5px 0;'><strong>Queue:</strong> {student.Queue.QueueName}</p>
                                <p style='margin: 5px 0;'><strong>Queue Number:</strong> #{student.QueueNumber}</p>
                                <p style='margin: 5px 0;'><strong>Status:</strong> <span style='color: #28a745;'>Completed</span></p>
                            </div>
                            <p>Thank you for your patience!</p>
                            <hr/>
                            <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                        </div>"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Email error: {ex.Message}");
                }

                return Json(new
                {
                    success = true,
                    message = $"✅ {student.Student.FirstName} {student.Student.LastName} completed (manually)!",
                    studentName = $"{student.Student.FirstName} {student.Student.LastName}",
                    queueNumber = student.QueueNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // REPORTS
        // =====================
        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Advance"))
                return RedirectToAction("Dashboard");

            var queues = await _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .OrderByDescending(q => q.DateCreated)
                .ToListAsync();

            return View(queues);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadQueueReportCsv(int queueId)
        {
            var queue = await _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .FirstOrDefaultAsync(q => q.QueueID == queueId);

            if (queue == null)
                return NotFound();

            var csv = GenerateCsvReport(queue);
            var fileName = $"Queue_Report_{queue.QueueName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(
                System.Text.Encoding.UTF8.GetBytes(csv),
                "text/csv",
                fileName
            );
        }

        private string GenerateCsvReport(Queue queue)
        {
            var csv = new StringBuilder();
            csv.AppendLine($"Queue Report: {queue.QueueName}");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"Created By: {queue.CreatedByAdmin.FirstName} {queue.CreatedByAdmin.LastName}");
            csv.AppendLine($"Date Created: {queue.DateCreated:yyyy-MM-dd HH:mm:ss}");
            if (queue.ScheduledEndTime.HasValue)
                csv.AppendLine($"Scheduled End Time: {queue.ScheduledEndTime.Value:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine();
            csv.AppendLine("Statistics:");
            csv.AppendLine($"Total Students,{queue.QueueStudents?.Count ?? 0}");
            csv.AppendLine($"Completed,{queue.QueueStudents?.Count(qs => qs.Status == "Done") ?? 0}");
            csv.AppendLine($"Pending,{queue.QueueStudents?.Count(qs => qs.Status == "Pending") ?? 0}");
            csv.AppendLine($"Serving,{queue.QueueStudents?.Count(qs => qs.Status == "Serving") ?? 0}");
            csv.AppendLine($"Unserved,{queue.QueueStudents?.Count(qs => qs.Status == "Unserved") ?? 0}");
            csv.AppendLine();
            csv.AppendLine("Queue Number,Name,Email,Joined At,Status,PIN Code");

            if (queue.QueueStudents != null)
            {
                foreach (var qs in queue.QueueStudents.OrderBy(s => s.QueueNumber))
                {
                    var name = $"{qs.Student.FirstName} {qs.Student.MiddleName} {qs.Student.LastName}".Trim();
                    csv.AppendLine($"{qs.QueueNumber},{name},{qs.Student.Email},{qs.JoinedAt:yyyy-MM-dd HH:mm:ss},{qs.Status},{qs.PinCode}");
                }
            }

            return csv.ToString();
        }

        // =====================
        // NOTIFICATIONS
        // =====================
        [HttpGet]
        public async Task<IActionResult> Notifications(int? queueId)
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Staff", "Advance"))
                return RedirectToAction("Dashboard");

            var queues = await _context.Queues
                .Where(q => !q.IsDone)
                .OrderByDescending(q => q.IsActive)
                .ThenByDescending(q => q.DateCreated)
                .Select(q => new { q.QueueID, q.QueueName, q.IsActive })
                .ToListAsync();

            ViewBag.Queues = queues;

            if (queueId == null)
                queueId = queues.FirstOrDefault(q => q.IsActive)?.QueueID ?? queues.FirstOrDefault()?.QueueID;

            ViewBag.SelectedQueueId = queueId ?? 0;

            if (queueId == null || queueId == 0)
                return View(new List<QueueStudent>());

            var students = await _context.QueueStudents
                .Include(qs => qs.Student)
                .Include(qs => qs.Queue)
                .Where(qs => qs.QueueID == queueId && qs.Status != "Done" && qs.Status != "Unserved")
                .OrderBy(qs => qs.QueueNumber)
                .ToListAsync();

            return View(students);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> TriggerPositionNotifications(int queueId)
        {
            try
            {
                var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                int sent = await notificationService.SendPositionNotifications(queueId);
                return Json(new { success = true, message = $"Sent {sent} position-based notification(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SendBroadcastNotification(
            int queueId, string title, string message, string notificationType)
        {
            try
            {
                var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                int sent = await notificationService.SendBroadcastNotification(queueId, title, message, notificationType);
                return Json(new { success = true, message = $"Broadcast sent to {sent} student(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SendManualNotification(
            int queueId, string studentIds, string title, string message, string notificationType)
        {
            try
            {
                var ids = studentIds.Split(',').Select(int.Parse).ToList();
                var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                int sent = await notificationService.SendManualNotification(queueId, ids, title, message, notificationType);
                return Json(new { success = true, message = $"Notification sent to {sent} student(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UpdateNotificationSettings(
            int queueId, bool enableAutoNotifications, int notifyAt20, int notifyAt10, int notifyAt3)
        {
            try
            {
                var notificationService = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                bool success = await notificationService.UpdateNotificationSettings(
                    queueId, enableAutoNotifications, notifyAt20, notifyAt10, notifyAt3);

                return success
                    ? Json(new { success = true, message = "Settings saved." })
                    : Json(new { success = false, message = "Queue not found." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // ARCHIVE
        // =====================
        [HttpGet]
        public async Task<IActionResult> Archive(string search, DateTime? startDate, DateTime? endDate, string sortBy = "recent")
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Advance"))
                return RedirectToAction("Dashboard");

            var query = _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .Where(q => q.IsDone);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(q =>
                    q.QueueName.ToLower().Contains(search) ||
                    q.Description.ToLower().Contains(search) ||
                    q.QueueCode.ToLower().Contains(search) ||
                    (q.CreatedByAdmin.FirstName + " " + q.CreatedByAdmin.LastName).ToLower().Contains(search)
                );
            }

            if (startDate.HasValue)
                query = query.Where(q => q.DateCreated >= startDate.Value);

            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(q => q.DateCreated <= endOfDay);
            }

            query = sortBy switch
            {
                "oldest" => query.OrderBy(q => q.DateCreated),
                "name" => query.OrderBy(q => q.QueueName),
                "students" => query.OrderByDescending(q => q.QueueStudents.Count),
                _ => query.OrderByDescending(q => q.DateCreated)
            };

            var archivedQueues = await query.ToListAsync();

            ViewBag.TotalArchived = archivedQueues.Count;
            ViewBag.TotalStudentsServed = archivedQueues.Sum(q => q.QueueStudents?.Count(qs => qs.Status == "Done") ?? 0);
            ViewBag.TotalStudentsUnserved = archivedQueues.Sum(q => q.QueueStudents?.Count(qs => qs.Status == "Unserved") ?? 0);
            ViewBag.AverageStudentsPerQueue = archivedQueues.Any()
                ? Math.Round(archivedQueues.Average(q => q.QueueStudents?.Count ?? 0), 1)
                : 0;
            ViewBag.SearchTerm = search;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.SortBy = sortBy;

            return View(archivedQueues);
        }

        [HttpGet]
        public async Task<IActionResult> ArchiveDetails(int id)
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Advance"))
                return RedirectToAction("Dashboard");

            var queue = await _context.Queues
                .Include(q => q.CreatedByAdmin)
                .Include(q => q.QueueStudents)
                    .ThenInclude(qs => qs.Student)
                .FirstOrDefaultAsync(q => q.QueueID == id && q.IsDone);

            if (queue == null)
            {
                TempData["Error"] = "Archived queue not found.";
                return RedirectToAction("Archive");
            }

            var students = queue.QueueStudents ?? new List<QueueStudent>();
            ViewBag.TotalStudents = students.Count;
            ViewBag.CompletedCount = students.Count(s => s.Status == "Done");
            ViewBag.UnservedCount = students.Count(s => s.Status == "Unserved");
            ViewBag.CompletionRate = students.Any()
                ? Math.Round((students.Count(s => s.Status == "Done") * 100.0) / students.Count, 1)
                : 0;

            if (queue.DateCompleted.HasValue)
            {
                var duration = queue.DateCompleted.Value - queue.DateCreated;
                ViewBag.Duration = $"{duration.Days}d {duration.Hours}h {duration.Minutes}m";
            }

            return View(queue);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PermanentlyDeleteArchive(int id)
        {
            if (!RequireRole("SuperAdmin"))
                return Json(new { success = false, message = "Only SuperAdmin can permanently delete archived queues." });

            try
            {
                var queue = await _context.Queues
                    .Include(q => q.QueueStudents)
                    .FirstOrDefaultAsync(q => q.QueueID == id && q.IsDone);

                if (queue == null)
                    return Json(new { success = false, message = "Archived queue not found." });

                var queueName = queue.QueueName;
                var studentCount = queue.QueueStudents?.Count ?? 0;

                if (queue.QueueStudents != null && queue.QueueStudents.Any())
                    _context.QueueStudents.RemoveRange(queue.QueueStudents);

                _context.Queues.Remove(queue);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Queue '{queueName}' and {studentCount} student record(s) permanently deleted."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkRestoreArchives(string queueIds)
        {
            if (!RequireRole("SuperAdmin", "Registrar"))
                return Json(new { success = false, message = "Insufficient permissions." });

            try
            {
                var ids = queueIds.Split(',').Select(int.Parse).ToList();
                var queues = await _context.Queues
                    .Include(q => q.QueueStudents)
                    .Where(q => ids.Contains(q.QueueID) && q.IsDone)
                    .ToListAsync();

                if (!queues.Any())
                    return Json(new { success = false, message = "No valid archived queues found." });

                foreach (var queue in queues)
                {
                    queue.IsDone = false;
                    queue.IsActive = false;
                    queue.Status = "Pending";
                    queue.DateCompleted = null;

                    var unservedStudents = queue.QueueStudents?.Where(s => s.Status == "Unserved").ToList();
                    if (unservedStudents != null)
                    {
                        foreach (var student in unservedStudents)
                        {
                            student.Status = "Pending";
                            student.IsUnserved = false;
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Successfully restored {queues.Count} queue(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // DELETE QUEUE
        // =====================
        [HttpGet]
        public async Task<IActionResult> DeleteQueue(int id)
        {
            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
            {
                TempData["Error"] = "Queue not found.";
                return RedirectToAction("ManageQueues");
            }

            if (queue.IsActive)
            {
                TempData["Error"] = "Cannot delete an active queue. Mark it as done first.";
                return RedirectToAction("ManageQueues");
            }

            if (queue.IsDone)
            {
                TempData["Info"] = "This queue is archived. Use the Archive page to manage it.";
                return RedirectToAction("Archive");
            }

            var hasStudents = queue.QueueStudents?.Any() ?? false;

            try
            {
                if (!hasStudents)
                {
                    _context.Queues.Remove(queue);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Queue '{queue.QueueName}' deleted successfully.";
                }
                else
                {
                    queue.IsDone = true;
                    queue.IsActive = false;
                    queue.Status = "Done";
                    queue.DateCompleted = DateTime.UtcNow;

                    foreach (var student in queue.QueueStudents.Where(s => s.Status != "Done"))
                    {
                        student.Status = "Unserved";
                        student.IsUnserved = true;
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Queue '{queue.QueueName}' archived (contains student data).";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + ex.Message;
            }

            return RedirectToAction("ManageQueues");
        }

        // =====================
        // MANAGE ADMINS
        // =====================
        [HttpGet]
        public async Task<IActionResult> ManageAdmins(string search, string roleFilter, string sortBy = "recent")
        {
            if (!RequireRole("SuperAdmin"))
            {
                TempData["Error"] = "Only SuperAdmin can manage administrators.";
                return RedirectToAction("Dashboard");
            }

            var currentAdminId = HttpContext.Session.GetInt32("AdminID") ?? 0;
            var query = _context.Admins.Where(a => a.AdminID != currentAdminId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(a =>
                    a.FirstName.ToLower().Contains(search) ||
                    a.LastName.ToLower().Contains(search) ||
                    a.Email.ToLower().Contains(search) ||
                    (a.MiddleName != null && a.MiddleName.ToLower().Contains(search))
                );
            }

            if (!string.IsNullOrWhiteSpace(roleFilter))
                query = query.Where(a => a.Role == roleFilter);

            query = sortBy switch
            {
                "name" => query.OrderBy(a => a.FirstName).ThenBy(a => a.LastName),
                "email" => query.OrderBy(a => a.Email),
                "role" => query.OrderBy(a => a.Role).ThenBy(a => a.FirstName),
                "oldest" => query.OrderBy(a => a.CreatedAt),
                _ => query.OrderByDescending(a => a.CreatedAt)
            };

            var admins = await query.ToListAsync();

            ViewBag.TotalAdmins = admins.Count + 1;
            ViewBag.SuperAdminCount = await _context.Admins.CountAsync(a => a.Role == "SuperAdmin");
            ViewBag.RegistrarCount = await _context.Admins.CountAsync(a => a.Role == "Registrar");
            ViewBag.StaffCount = await _context.Admins.CountAsync(a => a.Role == "Staff");
            ViewBag.AdvanceCount = await _context.Admins.CountAsync(a => a.Role == "Advance");
            ViewBag.CurrentAdminId = currentAdminId;
            ViewBag.SearchTerm = search;
            ViewBag.RoleFilter = roleFilter;
            ViewBag.SortBy = sortBy;

            return View(admins);
        }

        [HttpGet]
        public async Task<IActionResult> AdminDetails(int id)
        {
            if (!RequireRole("SuperAdmin"))
            {
                TempData["Error"] = "Only SuperAdmin can view admin details.";
                return RedirectToAction("Dashboard");
            }

            var admin = await _context.Admins.FindAsync(id);

            if (admin == null)
            {
                TempData["Error"] = "Administrator not found.";
                return RedirectToAction("ManageAdmins");
            }

            var queuesCreated = await _context.Queues
                .Where(q => q.CreatedByAdminID == id)
                .OrderByDescending(q => q.DateCreated)
                .Take(10)
                .ToListAsync();

            ViewBag.QueuesCreated = queuesCreated;
            ViewBag.TotalQueuesCreated = await _context.Queues.CountAsync(q => q.CreatedByAdminID == id);
            ViewBag.ActiveQueuesCreated = await _context.Queues.CountAsync(q => q.CreatedByAdminID == id && q.IsActive);
            ViewBag.CompletedQueuesCreated = await _context.Queues.CountAsync(q => q.CreatedByAdminID == id && q.IsDone);
            ViewBag.TotalStudentsServed = await _context.Queues
                .Where(q => q.CreatedByAdminID == id)
                .SelectMany(q => q.QueueStudents)
                .CountAsync(qs => qs.Status == "Done");

            return View(admin);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> RemoveAdminAjax(int id)
        {
            if (!RequireRole("SuperAdmin"))
                return Json(new { success = false, message = "Only SuperAdmin can remove administrators." });

            try
            {
                var currentAdminId = HttpContext.Session.GetInt32("AdminID") ?? 0;

                if (id == currentAdminId)
                    return Json(new { success = false, message = "You cannot remove your own account." });

                var admin = await _context.Admins.FindAsync(id);
                if (admin == null)
                    return Json(new { success = false, message = "Administrator not found." });

                var activeQueues = await _context.Queues
                    .CountAsync(q => q.CreatedByAdminID == id && q.IsActive);

                if (activeQueues > 0)
                    return Json(new
                    {
                        success = false,
                        message = $"Cannot remove admin. They have {activeQueues} active queue(s)."
                    });

                var adminName = $"{admin.FirstName} {admin.LastName}";
                _context.Admins.Remove(admin);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Administrator '{adminName}' removed successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UpdateAdminRoleAjax(int id, string newRole)
        {
            if (!RequireRole("SuperAdmin"))
                return Json(new { success = false, message = "Only SuperAdmin can update admin roles." });

            try
            {
                var currentAdminId = HttpContext.Session.GetInt32("AdminID") ?? 0;

                if (id == currentAdminId)
                    return Json(new { success = false, message = "You cannot change your own role." });

                var admin = await _context.Admins.FindAsync(id);
                if (admin == null)
                    return Json(new { success = false, message = "Administrator not found." });

                var validRoles = new[] { "SuperAdmin", "Registrar", "Staff", "Advance" };
                if (!validRoles.Contains(newRole))
                    return Json(new { success = false, message = "Invalid role." });

                var oldRole = admin.Role;
                admin.Role = newRole;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Role updated from {oldRole} to {newRole}.", newRole });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // REGISTRATION (SuperAdmin Only)
        // =====================
        [HttpGet]
        public IActionResult Register()
        {
            if (!RequireRole("SuperAdmin"))
                return Unauthorized("Only Super Admins can register new admins.");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(AdminRegisterViewModel model, string VerificationCode)
        {
            if (!RequireRole("SuperAdmin"))
                return Unauthorized("Only Super Admins can register new admins.");

            if (!ModelState.IsValid) return View(model);

            if (await _context.Admins.AnyAsync(a => a.Email == model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Email already exists.");
                return View(model);
            }

            var storedCode = HttpContext.Session.GetString("EmailVerificationCode");
            var storedEmail = HttpContext.Session.GetString("EmailVerificationEmail");
            var expiryString = HttpContext.Session.GetString("EmailVerificationExpiry");

            if (string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(storedEmail) || string.IsNullOrEmpty(expiryString))
            {
                ModelState.AddModelError("Email", "Please request a verification code first.");
                return View(model);
            }

            if (!string.Equals(model.Email, storedEmail, StringComparison.OrdinalIgnoreCase) || storedCode != VerificationCode)
            {
                ModelState.AddModelError("VerificationCode", "Invalid verification code.");
                return View(model);
            }

            if (!DateTime.TryParse(expiryString, out var expiryUtc) || DateTime.UtcNow > expiryUtc)
            {
                ModelState.AddModelError("VerificationCode", "Verification code has expired.");
                return View(model);
            }

            var admin = new Admin
            {
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                FirstName = model.FirstName,
                MiddleName = model.MiddleName,
                LastName = model.LastName,
                Role = model.Role,
                CreatedAt = DateTime.UtcNow
            };

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();

            HttpContext.Session.Remove("EmailVerificationCode");
            HttpContext.Session.Remove("EmailVerificationEmail");
            HttpContext.Session.Remove("EmailVerificationExpiry");

            TempData["Success"] = $"Admin {model.Email} registered successfully!";
            return RedirectToAction("Dashboard");
        }

        // =====================
        // EMAIL VERIFICATION
        // =====================
        [HttpPost]
        public async Task<IActionResult> GetVerificationCode([FromForm] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { success = false, message = "Email is required." });

            try
            {
                var code = new Random().Next(100000, 999999).ToString("D6");

                HttpContext.Session.SetString("EmailVerificationCode", code);
                HttpContext.Session.SetString("EmailVerificationEmail", email);
                HttpContext.Session.SetString("EmailVerificationExpiry", DateTime.UtcNow.AddMinutes(5).ToString("o"));

                var emailBody = $@"
                    <div style='text-align:center; font-family: Arial, sans-serif;'>
                        <h2>Patao NHS Smart Queuing</h2>
                        <h1 style='color:#28a745; font-size:48px; letter-spacing:6px;'>{code}</h1>
                        <p>This code will expire in 5 minutes.</p>
                        <hr/>
                        <small>If you didn't request this, please ignore this email.</small>
                    </div>";

                await _emailService.SendEmailAsync(email, "Admin Verification Code", emailBody);

                return Ok(new { success = true, message = "Verification code sent!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult VerifyCode([FromForm] string code)
        {
            var savedCode = HttpContext.Session.GetString("EmailVerificationCode");
            var expiry = HttpContext.Session.GetString("EmailVerificationExpiry");

            if (string.IsNullOrEmpty(savedCode) || string.IsNullOrEmpty(expiry))
                return Json(new { success = false, message = "No verification code found." });

            if (!DateTime.TryParse(expiry, out var expiryUtc) || DateTime.UtcNow > expiryUtc)
                return Json(new { success = false, message = "Code expired. Please request a new one." });

            if (savedCode != code)
                return Json(new { success = false, message = "Invalid verification code." });

            HttpContext.Session.SetString("EmailVerified", "true");
            return Json(new { success = true, message = "Email verified successfully!" });
        }

        // =====================
        // SETTINGS
        // =====================
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            if (!RequireRole("SuperAdmin", "Registrar", "Staff", "Advance"))
                return RedirectToAction("Login", new { token = _config["AdminSettings:PortalToken"] });

            var adminId = HttpContext.Session.GetInt32("AdminID") ?? 0;
            var admin = await _context.Admins.FindAsync(adminId);

            if (admin == null)
            {
                TempData["Error"] = "Admin not found. Please log in again.";
                return RedirectToAction("Logout");
            }

            ViewBag.QueuesCreated = await _context.Queues.CountAsync(q => q.CreatedByAdminID == adminId);
            ViewBag.ActiveQueues = await _context.Queues.CountAsync(q => q.CreatedByAdminID == adminId && q.IsActive);
            ViewBag.CompletedQueues = await _context.Queues.CountAsync(q => q.CreatedByAdminID == adminId && q.IsDone);

            if (RequireRole("SuperAdmin"))
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                ViewBag.PortalToken = settings?.PortalToken ?? "Not Set";
            }

            return View(admin);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            try
            {
                var adminId = HttpContext.Session.GetInt32("AdminID") ?? 0;
                var admin = await _context.Admins.FindAsync(adminId);

                if (admin == null)
                    return Json(new { success = false, message = "Admin not found." });

                if (!BCrypt.Net.BCrypt.Verify(currentPassword, admin.PasswordHash))
                    return Json(new { success = false, message = "Current password is incorrect." });

                if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                    return Json(new { success = false, message = "New password must be at least 8 characters." });

                if (newPassword != confirmPassword)
                    return Json(new { success = false, message = "New passwords do not match." });

                admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.SendEmailAsync(
                        admin.Email,
                        "Password Changed – Patao NHS Smart Queuing",
                        $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                            <h2>Password Changed</h2>
                            <p>Hello <strong>{admin.FirstName}</strong>,</p>
                            <p>Your password was changed on {DateTime.Now:MMM dd, yyyy hh:mm tt}.</p>
                            <p>If you did not make this change, contact the system administrator immediately.</p>
                            <hr/>
                            <small style='color: #6c757d;'>Patao NHS Smart Queuing System</small>
                        </div>"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Email error: {ex.Message}");
                }

                return Json(new { success = true, message = "Password changed successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePortalToken(string newToken)
        {
            if (!RequireRole("SuperAdmin"))
                return Json(new { success = false, message = "Only SuperAdmin can update portal token." });

            try
            {
                if (string.IsNullOrWhiteSpace(newToken))
                    return Json(new { success = false, message = "Token cannot be empty." });

                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new AdminSettings { PortalToken = newToken.Trim(), UpdatedAt = DateTime.UtcNow };
                    _context.AdminSettings.Add(settings);
                }
                else
                {
                    settings.PortalToken = newToken.Trim();
                    settings.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Portal token updated!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // =====================
        // CHANGE SEARCH TOKEN (dedicated page — SuperAdmin only)
        // =====================
        [HttpGet]
        public async Task<IActionResult> ChangeSearchToken()
        {
            if (!RequireRole("SuperAdmin"))
            {
                TempData["Error"] = "Only SuperAdmin can change the portal token.";
                return RedirectToAction("Dashboard");
            }

            var settings = await _context.AdminSettings.FirstOrDefaultAsync();
            return View(settings ?? new AdminSettings());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeSearchToken(string newToken)
        {
            if (!RequireRole("SuperAdmin"))
            {
                TempData["Error"] = "Only SuperAdmin can change the portal token.";
                return RedirectToAction("Dashboard");
            }

            if (string.IsNullOrWhiteSpace(newToken))
            {
                TempData["Error"] = "Token cannot be empty.";
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                return View(settings ?? new AdminSettings());
            }

            var adminSettings = await _context.AdminSettings.FirstOrDefaultAsync();
            if (adminSettings == null)
            {
                adminSettings = new AdminSettings
                {
                    PortalToken = newToken.Trim(),
                    UpdatedAt = DateTime.UtcNow
                };
                _context.AdminSettings.Add(adminSettings);
            }
            else
            {
                adminSettings.PortalToken = newToken.Trim();
                adminSettings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Portal token updated successfully!";
            return RedirectToAction("ChangeSearchToken");
        }

        // =====================
        // LIVE VIEW
        // =====================
        [HttpGet("LiveView")]
        public IActionResult LiveView(int? queueId)
        {
            if (queueId == null || queueId == 0)
            {
                var activeQueue = _context.Queues
                    .Where(q => q.IsActive && !q.IsDone)
                    .OrderByDescending(q => q.DateCreated)
                    .FirstOrDefault();

                if (activeQueue != null)
                    queueId = activeQueue.QueueID;
                else
                {
                    var latestQueue = _context.Queues
                        .Where(q => !q.IsDone)
                        .OrderByDescending(q => q.DateCreated)
                        .FirstOrDefault();

                    if (latestQueue != null)
                        queueId = latestQueue.QueueID;
                    else
                        return Content("No queues found. Please create a queue first.");
                }
            }

            var queue = _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefault(q => q.QueueID == queueId);

            if (queue == null)
                return Content($"Queue with ID {queueId} not found.");

            var servingStudents = queue.QueueStudents
                .Where(q => q.IsServing && !q.IsDone && !q.IsUnserved)
                .OrderBy(q => q.QueueNumber)
                .ToList();

            var nextInLine = queue.QueueStudents
                .Where(q => (q.Status == "Waiting" || q.Status == "Pending") && !q.IsDone && !q.IsUnserved)
                .OrderBy(q => q.QueueNumber)
                .Take(5)
                .ToList();

            ViewBag.QueueName = queue.QueueName;
            ViewBag.QueueId = queueId;
            ViewBag.ServingStudents = servingStudents;
            ViewBag.NextInLine = nextInLine;
            ViewBag.IsActive = queue.IsActive;

            if (queue.ScheduledEndTime.HasValue)
                ViewBag.ScheduledEndTime = queue.ScheduledEndTime.Value.ToString("MMM dd, yyyy hh:mm tt");

            return View();
        }

        // =====================
        // PORTAL
        // =====================
        [HttpGet("Admin/Portal/{token}")]
        public async Task<IActionResult> Portal(string token)
        {
            var portalTokenFromDb = await _context.AdminSettings
                .Select(a => a.PortalToken)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(token) || token != portalTokenFromDb)
                return Unauthorized("Access denied. Invalid portal token.");

            if (HttpContext.Session.GetInt32("StudentID") != null)
                return RedirectToAction("Dashboard", "Student");

            return View("Login");
        }

        // =====================
        // HELPER: GENERATE UNIQUE CODE
        // =====================
        private string GenerateUniqueQueueCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            string code;

            do
            {
                code = new string(Enumerable.Repeat(chars, 6)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
            }
            while (_context.Queues.Any(q => q.QueueCode == code));

            return code;
        }
    }
}