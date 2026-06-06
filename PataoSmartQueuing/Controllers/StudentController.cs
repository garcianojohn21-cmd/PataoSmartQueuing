// ============================================
// COMPLETE STUDENT CONTROLLER
// ============================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Models;
using PataoSmartQueuing.Models.ViewModels;
using PataoSmartQueuing.ViewModels;
using PataoSmartQueuing.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using WebPush;

namespace PataoSmartQueuing.Controllers
{
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StudentController> _logger;
        private readonly PasswordChangeEmailService _passwordChangeEmailService;

        public StudentController(
            ApplicationDbContext context,
            ILogger<StudentController> logger,
            PasswordChangeEmailService passwordChangeEmailService)
        {
            _context = context;
            _logger = logger;
            _passwordChangeEmailService = passwordChangeEmailService;
        }

        private bool IsLoggedIn()
        {
            return HttpContext.Session.GetInt32("StudentID") != null;
        }

        // ✅ FIXED Dashboard - Shows empty state when no queue
        public async Task<IActionResult> Dashboard()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
            {
                _logger.LogWarning("Unauthorized dashboard access attempt");
                return RedirectToAction("Login", "Account");
            }

            _logger.LogInformation($"Dashboard accessed by student ID: {studentId}");

            // Get the student's active queue participation
            var queueStudent = await _context.QueueStudents
                .Include(qs => qs.Queue)
                .Include(qs => qs.Queue.QueueStudents)
                .Include(qs => qs.Student)
                .FirstOrDefaultAsync(qs => qs.StudentID == studentId
                    && qs.Status != "Done"
                    && qs.Status != "Unserved");

            // ✅ No active queue → Show dashboard with empty state (DON'T REDIRECT)
            if (queueStudent == null)
            {
                _logger.LogInformation($"Student {studentId} has no active queue");

                // Check if they were just marked as done (within last 5 minutes)
                var justDoneStudent = await _context.QueueStudents
                    .Include(qs => qs.Queue)
                    .Where(qs => qs.StudentID == studentId && qs.Status == "Done")
                    .OrderByDescending(qs => qs.JoinedAt)
                    .FirstOrDefaultAsync();

                if (justDoneStudent != null &&
                    (DateTime.Now - justDoneStudent.JoinedAt).TotalMinutes < 5)
                {
                    ViewBag.JustServed = true;
                    ViewBag.QueueName = justDoneStudent.Queue?.QueueName;
                }

                // ✅ Show dashboard with no queue status
                ViewBag.QueueJoined = false;
                ViewBag.NoActiveQueue = true;
                return View();
            }

            var queue = queueStudent.Queue;

            // Queue was deleted or marked as done by admin
            if (queue == null || queue.IsDone)
            {
                _logger.LogWarning($"Queue {queueStudent.QueueID} no longer available for student {studentId}");

                if (queueStudent != null)
                {
                    _context.QueueStudents.Remove(queueStudent);
                    await _context.SaveChangesAsync();
                }

                ViewBag.QueueJoined = false;
                ViewBag.QueueDeleted = true;
                ViewBag.Message = "The queue you were in is no longer available.";
                return View();
            }

            // Double-check student status
            if (queueStudent.Status == "Done" || queueStudent.IsDone)
            {
                _logger.LogInformation($"Student {studentId} marked as done");
                ViewBag.JustServed = true;
                ViewBag.QueueName = queue.QueueName;
                ViewBag.QueueJoined = false;
                return View();
            }

            if (queueStudent.Status == "Unserved" || queueStudent.IsUnserved)
            {
                _logger.LogWarning($"Student {studentId} was marked as unserved");
                ViewBag.QueueJoined = false;
                ViewBag.WasUnserved = true;
                ViewBag.Message = "You were marked as unserved. Please join the queue again.";
                return View();
            }

            // Count how many joined this queue
            var totalJoined = queue.QueueStudents.Count(qs => qs.Status != "Unserved");

            // Get all students currently being served
            var servingStudents = queue.QueueStudents
                .Where(qs => qs.Status == "Serving")
                .OrderBy(qs => qs.QueueNumber)
                .Select(qs => qs.QueueNumber)
                .ToList();

            string servingDisplay = servingStudents.Count > 0
                ? string.Join(", ", servingStudents.Select(n => $"#{n}"))
                : "-";

            // Send data to View
            ViewBag.QueueJoined = true;
            ViewBag.QueueName = queue.QueueName;
            ViewBag.PriorityNumber = queueStudent.QueueNumber;
            ViewBag.CurrentServing = servingDisplay;
            ViewBag.ServingNumbers = servingStudents;
            ViewBag.TotalJoined = totalJoined;
            ViewBag.PinCode = queueStudent.PinCode;
            ViewBag.QueueID = queue.QueueID;
            ViewBag.QueueStudentId = queueStudent.QueueStudentID;
            ViewBag.StudentStatus = queueStudent.Status;
            ViewBag.StudentID = studentId;

            _logger.LogInformation($"Dashboard loaded for student {studentId} in queue {queue.QueueID}");

            return View();
        }

        // ✅ FIXED GetQueueStatus
        [HttpGet]
        public async Task<IActionResult> GetQueueStatus(int id)
        {
            _logger.LogInformation($"GetQueueStatus called for queue ID: {id}");

            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueID == id);

            if (queue == null)
            {
                _logger.LogWarning($"Queue {id} not found");
                return NotFound(new { error = "Queue not found", queueNotFound = true });
            }

            if (queue.IsDone)
            {
                _logger.LogInformation($"Queue {id} is marked as done");
                return Json(new { error = "Queue is done", queueDeleted = true });
            }

            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
            {
                _logger.LogWarning("GetQueueStatus called without student session");
                return Json(new { error = "Not logged in" });
            }

            var student = queue.QueueStudents.FirstOrDefault(s => s.StudentID == studentId);

            if (student == null)
            {
                _logger.LogWarning($"Student {studentId} not found in queue {id}");
                return Json(new { error = "Not in queue", queueNotFound = true });
            }

            if (student.Status == "Unserved" || student.IsUnserved)
            {
                _logger.LogInformation($"Student {studentId} is marked as unserved");
                return Json(new { error = "Unserved", isStudentUnserved = true });
            }

            var servingStudents = queue.QueueStudents
                .Where(qs => qs.Status == "Serving")
                .OrderBy(qs => qs.QueueNumber)
                .Select(qs => qs.QueueNumber)
                .ToList();

            bool isStudentDone = student.Status == "Done" || student.IsDone;

            _logger.LogInformation($"Queue status for student {studentId}: Status={student.Status}, Done={isStudentDone}");

            return Json(new
            {
                queueName = queue.QueueName,
                priorityNumber = student.QueueNumber,
                servingNumbers = servingStudents,
                totalJoined = queue.QueueStudents.Count(qs => qs.Status != "Unserved"),
                isDone = isStudentDone,
                isStudentDone = isStudentDone,
                studentStatus = student.Status,
                queueStudentId = student.QueueStudentID
            });
        }

        // ✅ FIXED History Page
        [HttpGet]
        public async Task<IActionResult> History()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
                return RedirectToAction("Login", "Account");

            _logger.LogInformation($"History accessed by student {studentId}");

            var history = await _context.QueueStudents
                .Include(qs => qs.Queue)
                .Where(qs => qs.StudentID == studentId
                    && (qs.Status == "Done" || qs.Status == "Unserved"))
                .OrderByDescending(qs => qs.JoinedAt)
                .ToListAsync();

            _logger.LogInformation($"Found {history.Count} history entries for student {studentId}");

            return View(history);
        }

        // ✅ COMPLETELY FIXED JoinQueue GET - NO MORE REDIRECTS!
        [HttpGet]
        public async Task<IActionResult> JoinQueue(string code)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
                return RedirectToAction("Login", "Account");

            _logger.LogInformation($"JoinQueue GET accessed by student {studentId}, code: {code ?? "none"}");

            // ✅ If NO code provided, just show the form (don't check for active queues)
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogInformation("No code provided, showing join queue form");
                return View();
            }

            // ✅ Code provided - validate the queue
            var queue = await _context.Queues
                .FirstOrDefaultAsync(q => q.QueueCode == code && q.IsActive && !q.IsDone);

            if (queue == null)
            {
                _logger.LogWarning($"Invalid queue code: {code}");
                TempData["Error"] = "Invalid or closed queue code.";
                return RedirectToAction("JoinQueue");
            }

            // ✅ Check if student has already joined this specific queue before
            var hasJoinedBefore = await _context.QueueStudents
                .AnyAsync(qs => qs.StudentID == studentId && qs.QueueID == queue.QueueID);

            if (hasJoinedBefore)
            {
                _logger.LogWarning($"Student {studentId} already joined queue {queue.QueueID} before");
                TempData["Error"] = "You have already joined this queue before. You cannot rejoin the same queue.";
                return RedirectToAction("JoinQueue");
            }

            // ✅ Check if student is currently in a DIFFERENT active queue
            var activeInOtherQueue = await _context.QueueStudents
                .Include(qs => qs.Queue)
                .FirstOrDefaultAsync(qs => qs.StudentID == studentId
                    && qs.QueueID != queue.QueueID
                    && qs.Status != "Done"
                    && qs.Status != "Unserved"
                    && !qs.Queue.IsDone);

            if (activeInOtherQueue != null)
            {
                _logger.LogWarning($"Student {studentId} is in another active queue: {activeInOtherQueue.QueueID}");
                TempData["Error"] = $"You are already in another queue: '{activeInOtherQueue.Queue.QueueName}'. Please complete that queue first.";
                return RedirectToAction("Dashboard");
            }

            // ✅ All checks passed - show queue details
            ViewBag.QueueCode = code;
            ViewBag.QueueName = queue.QueueName;
            _logger.LogInformation($"Showing queue details for: {queue.QueueName}");
            return View();
        }

        // ✅ FIXED JoinQueue POST
        [HttpPost]
        [ActionName("JoinQueue")]
        public async Task<IActionResult> JoinQueuePost(string code)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");

            _logger.LogInformation($"=== JoinQueuePost Started ===");
            _logger.LogInformation($"Student ID from session: {studentId}");
            _logger.LogInformation($"Queue code provided: {code}");

            if (studentId == null)
            {
                _logger.LogWarning("Student not logged in - redirecting to login");
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogWarning("No queue code provided");
                TempData["Error"] = "Please enter a queue code.";
                return RedirectToAction("JoinQueue");
            }

            _logger.LogInformation($"Searching for queue with code: {code}");

            var queue = await _context.Queues
                .Include(q => q.QueueStudents)
                .FirstOrDefaultAsync(q => q.QueueCode == code && q.IsActive && !q.IsDone);

            if (queue == null)
            {
                _logger.LogWarning($"Queue not found or not active. Code: {code}");

                var inactiveQueue = await _context.Queues
                    .FirstOrDefaultAsync(q => q.QueueCode == code);

                if (inactiveQueue != null)
                {
                    _logger.LogWarning($"Queue exists but IsActive={inactiveQueue.IsActive}, IsDone={inactiveQueue.IsDone}");
                    TempData["Error"] = $"Queue '{inactiveQueue.QueueName}' is not currently accepting students. Please ask admin to activate it.";
                }
                else
                {
                    _logger.LogWarning("Queue code not found in database");
                    TempData["Error"] = "Invalid queue code.";
                }

                return RedirectToAction("JoinQueue");
            }

            _logger.LogInformation($"✅ Queue found: ID={queue.QueueID}, Name={queue.QueueName}");
            _logger.LogInformation($"Checking if student {studentId} already joined queue {queue.QueueID}");

            var hasJoinedBefore = await _context.QueueStudents
                .AnyAsync(qs => qs.StudentID == studentId && qs.QueueID == queue.QueueID);

            if (hasJoinedBefore)
            {
                _logger.LogWarning($"Student {studentId} already joined queue {queue.QueueID} before");
                TempData["Error"] = "You have already joined this queue before. You cannot rejoin the same queue.";
                return RedirectToAction("JoinQueue");
            }

            _logger.LogInformation($"Checking if student {studentId} is in another active queue");

            var otherActiveQueue = await _context.QueueStudents
                .Include(qs => qs.Queue)
                .FirstOrDefaultAsync(qs => qs.StudentID == studentId
                    && qs.QueueID != queue.QueueID
                    && qs.Status != "Done"
                    && qs.Status != "Unserved"
                    && !qs.Queue.IsDone);

            if (otherActiveQueue != null)
            {
                _logger.LogWarning($"Student {studentId} is in another active queue: {otherActiveQueue.QueueID}");
                TempData["Error"] = $"You are already in another queue: '{otherActiveQueue.Queue.QueueName}'. Please complete that queue first.";
                return RedirectToAction("Dashboard");
            }

            _logger.LogInformation($"Calculating queue number for queue {queue.QueueID}");
            _logger.LogInformation($"Total students in queue.QueueStudents collection: {queue.QueueStudents?.Count ?? 0}");

            int queueNumber = queue.QueueStudents.Count(qs => qs.Status != "Unserved") + 1;

            _logger.LogInformation($"Calculated queue number: {queueNumber}");

            Random random = new Random();
            string pinCode = random.Next(100000, 999999).ToString();

            _logger.LogInformation($"Generated numeric PIN code: {pinCode}");

            var queueStudent = new QueueStudent
            {
                QueueID = queue.QueueID,
                StudentID = studentId.Value,
                QueueNumber = queueNumber,
                PinCode = pinCode,
                JoinedAt = DateTime.Now,
                Status = "Pending",
                IsServing = false,
                IsDone = false,
                IsUnserved = false
            };

            _logger.LogInformation($"QueueStudent object created:");
            _logger.LogInformation($"  - QueueID: {queueStudent.QueueID}");
            _logger.LogInformation($"  - StudentID: {queueStudent.StudentID}");
            _logger.LogInformation($"  - QueueNumber: {queueStudent.QueueNumber}");
            _logger.LogInformation($"  - Status: {queueStudent.Status}");
            _logger.LogInformation($"  - PinCode: {queueStudent.PinCode}");
            _logger.LogInformation($"  - JoinedAt: {queueStudent.JoinedAt}");

            try
            {
                _logger.LogInformation("Adding QueueStudent to database...");
                _context.QueueStudents.Add(queueStudent);

                _logger.LogInformation("Saving changes to database...");
                int savedCount = await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ SaveChanges completed. Rows affected: {savedCount}");
                _logger.LogInformation($"✅ QueueStudent ID assigned: {queueStudent.QueueStudentID}");

                var verification = await _context.QueueStudents
                    .FirstOrDefaultAsync(qs => qs.QueueStudentID == queueStudent.QueueStudentID);

                if (verification != null)
                {
                    _logger.LogInformation($"✅ VERIFICATION SUCCESS: Student found in database with ID {verification.QueueStudentID}");
                    _logger.LogInformation($"   Queue: {verification.QueueID}, Student: {verification.StudentID}, Number: {verification.QueueNumber}, Status: {verification.Status}");
                }
                else
                {
                    _logger.LogError($"❌ VERIFICATION FAILED: Student not found in database after save!");
                }

                TempData["Success"] = $"You joined '{queue.QueueName}'. Number: {queueNumber}, Pin: {pinCode}";

                _logger.LogInformation($"=== JoinQueuePost Completed Successfully ===");
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ EXCEPTION during save: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                }

                TempData["Error"] = "An error occurred while joining the queue. Please try again.";
                return RedirectToAction("JoinQueue");
            }
        }

        /// <summary>
        /// Verify if a student was successfully added to queue
        /// Call this from browser console: /Student/VerifyInQueue?studentId=1&queueId=1009
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> VerifyInQueue(int studentId, int queueId)
        {
            try
            {
                var queueStudent = await _context.QueueStudents
                    .Include(qs => qs.Queue)
                    .Include(qs => qs.Student)
                    .FirstOrDefaultAsync(qs => qs.StudentID == studentId && qs.QueueID == queueId);

                if (queueStudent == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Student {studentId} NOT found in queue {queueId}",
                        studentId = studentId,
                        queueId = queueId
                    });
                }

                return Json(new
                {
                    success = true,
                    message = "Student found in queue!",
                    data = new
                    {
                        queueStudentId = queueStudent.QueueStudentID,
                        queueId = queueStudent.QueueID,
                        queueName = queueStudent.Queue?.QueueName,
                        studentId = queueStudent.StudentID,
                        studentName = queueStudent.Student != null
                            ? $"{queueStudent.Student.FirstName} {queueStudent.Student.LastName}"
                            : "Unknown",
                        queueNumber = queueStudent.QueueNumber,
                        status = queueStudent.Status,
                        pinCode = queueStudent.PinCode,
                        joinedAt = queueStudent.JoinedAt,
                        isServing = queueStudent.IsServing,
                        isDone = queueStudent.IsDone,
                        isUnserved = queueStudent.IsUnserved
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error: " + ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
            {
                _logger.LogWarning("Unauthorized profile access attempt");
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
            {
                _logger.LogWarning($"Student {studentId} not found in database");
                return RedirectToAction("Login", "Account");
            }

            var model = new StudentProfileViewModel
            {
                StudentID = student.StudentID,
                FirstName = student.FirstName,
                MiddleName = student.MiddleName,
                LastName = student.LastName,
                Email = student.Email,
                LRN = student.LRN,
                GradeLevel = student.GradeLevel,
                Strand = student.Strand,
                ProfilePhoto = student.ProfilePhoto
            };

            _logger.LogInformation($"Profile loaded for student {studentId}");

            return View(model);
        }

        // ✅ GET EditProfile
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null) return RedirectToAction("Login", "Account");

            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return RedirectToAction("Login", "Account");

            var model = new StudentProfileViewModel
            {
                StudentID = student.StudentID,
                FirstName = student.FirstName,
                MiddleName = student.MiddleName,
                LastName = student.LastName,
                Email = student.Email,
                LRN = student.LRN,
                GradeLevel = student.GradeLevel,
                Strand = student.Strand,
                ProfilePhoto = student.ProfilePhoto
            };

            return View(model);
        }

        // ✅ POST EditProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(StudentProfileViewModel model)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null) return RedirectToAction("Login", "Account");

            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return RedirectToAction("Login", "Account");

            if (model.ProfileImage != null && model.ProfileImage.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/profiles");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ProfileImage.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ProfileImage.CopyToAsync(stream);
                }

                student.ProfilePhoto = "~/Images/profiles/" + uniqueFileName;
                _context.Update(student);
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "Profile photo updated successfully!";
            return RedirectToAction("Profile");
        }

        public IActionResult Logout()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            _logger.LogInformation($"Student {studentId} logged out");

            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePushSubscription([FromBody] PushSubscriptionDto subscriptionDto)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
                return Unauthorized();

            try
            {
                var existing = await _context.PushSubscriptions
                    .FirstOrDefaultAsync(s => s.StudentID == studentId && s.Endpoint == subscriptionDto.Endpoint);

                if (existing != null)
                {
                    existing.P256dh = subscriptionDto.P256dh;
                    existing.Auth = subscriptionDto.Auth;
                    existing.IsActive = true;
                    existing.CreatedAt = DateTime.Now;
                    _context.Update(existing);
                }
                else
                {
                    var subscription = new PataoSmartQueuing.Models.PushSubscription
                    {
                        StudentID = studentId.Value,
                        Endpoint = subscriptionDto.Endpoint,
                        P256dh = subscriptionDto.P256dh,
                        Auth = subscriptionDto.Auth,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };
                    _context.PushSubscriptions.Add(subscription);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"✅ Push subscription saved for student {studentId}");

                return Ok(new { success = true, message = "Subscription saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error saving push subscription: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error saving subscription" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnsubscribeFromPush()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
                return Unauthorized();

            try
            {
                var subscriptions = await _context.PushSubscriptions
                    .Where(s => s.StudentID == studentId)
                    .ToListAsync();

                foreach (var sub in subscriptions)
                {
                    sub.IsActive = false;
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"✅ Push subscriptions disabled for student {studentId}");

                return Ok(new { success = true, message = "Unsubscribed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error unsubscribing: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error unsubscribing" });
            }
        }

        public class PushSubscriptionDto
        {
            public string Endpoint { get; set; } = string.Empty;
            public string P256dh { get; set; } = string.Empty;
            public string Auth { get; set; } = string.Empty;
        }

        // ✅ GET Settings
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
            {
                _logger.LogWarning("Unauthorized settings access attempt");
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
            {
                _logger.LogWarning($"Student {studentId} not found in database");
                return RedirectToAction("Login", "Account");
            }

            var model = new StudentSettingsViewModel
            {
                StudentID = student.StudentID,
                Email = student.Email,
                FirstName = student.FirstName,
                LastName = student.LastName
            };

            _logger.LogInformation($"Settings page loaded for student {studentId}");

            return View(model);
        }

        // ✅ POST Update Password — with email notification
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
            {
                _logger.LogWarning("Unauthorized password update attempt");
                return RedirectToAction("Login", "Account");
            }

            // Validate inputs
            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["PasswordError"] = "All password fields are required.";
                return RedirectToAction("Settings");
            }

            if (newPassword != confirmPassword)
            {
                TempData["PasswordError"] = "New password and confirmation do not match.";
                return RedirectToAction("Settings");
            }

            if (newPassword.Length < 6)
            {
                TempData["PasswordError"] = "New password must be at least 6 characters long.";
                return RedirectToAction("Settings");
            }

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
            {
                _logger.LogWarning($"Student {studentId} not found");
                return RedirectToAction("Login", "Account");
            }

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, student.PasswordHash))
            {
                _logger.LogWarning($"Invalid current password attempt for student {studentId}");
                TempData["PasswordError"] = "Current password is incorrect.";
                return RedirectToAction("Settings");
            }

            // Update password
            try
            {
                student.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                _context.Update(student);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Password updated successfully for student {studentId}");

                // 📧 Send email notification (non-blocking — failure won't affect the user)
                await _passwordChangeEmailService.SendPasswordChangedNotificationAsync(
                    student.Email,
                    student.FirstName,
                    student.LastName
                );

                TempData["PasswordSuccess"] = "Password updated successfully! A confirmation email has been sent to your email address.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error updating password for student {studentId}: {ex.Message}");
                TempData["PasswordError"] = "An error occurred while updating your password. Please try again.";
            }

            return RedirectToAction("Settings");
        }

        // ✅ POST Delete Account
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAccount(string confirmPassword)
        {
            var studentId = HttpContext.Session.GetInt32("StudentID");
            if (studentId == null)
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["DeleteError"] = "Please enter your password.";
                return RedirectToAction("Settings");
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.StudentID == studentId);

            if (student == null)
                return RedirectToAction("Login", "Account");

            if (!BCrypt.Net.BCrypt.Verify(confirmPassword, student.PasswordHash))
            {
                TempData["DeleteError"] = "Incorrect password.";
                return RedirectToAction("Settings");
            }

            try
            {
                // ✅ Delete queue records
                var queueStudents = await _context.QueueStudents
                    .Where(qs => qs.StudentID == studentId)
                    .ToListAsync();

                _context.QueueStudents.RemoveRange(queueStudents);

                // ✅ Delete push subscriptions
                var pushSubs = await _context.PushSubscriptions
                    .Where(ps => ps.StudentID == studentId)
                    .ToListAsync();

                _context.PushSubscriptions.RemoveRange(pushSubs);

                // ✅ Delete student
                _context.Students.Remove(student);
                await _context.SaveChangesAsync();

                HttpContext.Session.Clear();
                TempData["Success"] = "Account deleted successfully.";
                return RedirectToAction("Login", "Account");
            }
            catch
            {
                TempData["DeleteError"] = "Failed to delete account.";
                return RedirectToAction("Settings");
            }
        }
    }
}