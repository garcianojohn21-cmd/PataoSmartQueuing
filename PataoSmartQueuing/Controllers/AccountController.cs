using Microsoft.AspNetCore.Mvc;
using PataoSmartQueuing.ViewModels;
using PataoSmartQueuing.Models;
using PataoSmartQueuing.Services;
using PataoSmartQueuing.Data;
using Microsoft.EntityFrameworkCore;

namespace PataoSmartQueuing.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public AccountController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // GET: Login
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // GET: Register
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // POST: Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var student = await _context.Students.FirstOrDefaultAsync(s => s.Email == model.Email);
            if (student == null)
            {
                ModelState.AddModelError("", "Email not found.");
                return View(model);
            }

            if (!model.UseVerificationCode)
            {
                if (!BCrypt.Net.BCrypt.Verify(model.Password, student.PasswordHash))
                {
                    ModelState.AddModelError("", "Incorrect password.");
                    return View(model);
                }
            }
            else
            {
                var sessionCode = HttpContext.Session.GetString("VerificationCode");
                var sessionEmail = HttpContext.Session.GetString("VerificationEmail");

                if (string.IsNullOrEmpty(model.VerificationCode))
                {
                    var code = new Random().Next(100000, 999999).ToString();
                    HttpContext.Session.SetString("VerificationCode", code);
                    HttpContext.Session.SetString("VerificationEmail", student.Email);

                    string emailBody = $@"
                    <div style='text-align:center; font-family: Arial, sans-serif;'>
                        <h2>Patao NHS Smart Queuing</h2>
                        <h1 style='color:#2AB366;'>{code}</h1>
                        <p>Use this code to login.</p>
                    </div>";

                    await _emailService.SendEmailAsync(student.Email, "Your Login Code", emailBody);
                    TempData["Message"] = "Verification code sent to your email.";
                    return RedirectToAction("Login");
                }

                if (model.VerificationCode != sessionCode || student.Email != sessionEmail)
                {
                    ModelState.AddModelError("", "Invalid verification code.");
                    return View(model);
                }
            }

            // Successful login
            HttpContext.Session.SetInt32("StudentID", student.StudentID);
            HttpContext.Session.SetString("StudentName", student.FirstName);
            HttpContext.Session.Remove("VerificationCode");
            HttpContext.Session.Remove("VerificationEmail");

            return RedirectToAction("Dashboard", "Student");
        }

        // POST: Send verification code via AJAX (used by Register page)
        [HttpPost]
        [Route("Account/GetCode")]
        public async Task<IActionResult> GetCode([FromForm] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { success = false, message = "Email is required." });

            // Prevent sending code to an already-registered email
            var emailExists = await _context.Students.AnyAsync(s => s.Email == email);
            if (emailExists)
                return Ok(new { success = false, message = "This email is already registered. Please login instead." });

            try
            {
                var code = new Random().Next(100000, 999999).ToString();
                HttpContext.Session.SetString("VerificationCode", code);
                HttpContext.Session.SetString("VerificationEmail", email);

                string emailBody = $@"
                <div style='text-align:center; font-family: Arial, sans-serif;'>
                    <h2>Patao NHS Smart Queuing</h2>
                    <h1 style='color:#2AB366;'>{code}</h1>
                    <p>Use this verification code to complete your registration.</p>
                    <p style='color:#888; font-size:12px;'>This code expires when a new one is requested.</p>
                </div>";

                await _emailService.SendEmailAsync(email, "Your Verification Code", emailBody);

                return Ok(new { success = true, message = "Verification code sent! Please check your email." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error sending code: {ex.Message}" });
            }
        }

        // POST: Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Validate verification code from session
            var storedCode = HttpContext.Session.GetString("VerificationCode");
            var storedEmail = HttpContext.Session.GetString("VerificationEmail");

            if (string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(storedEmail))
            {
                ModelState.AddModelError("", "Verification code expired or not requested. Please request a new code.");
                return View(model);
            }

            if (model.Email != storedEmail || model.VerificationCode != storedCode)
            {
                ModelState.AddModelError("", "Invalid verification code.");
                return View(model);
            }

            // Check if email already exists
            if (await _context.Students.AnyAsync(s => s.Email == model.Email))
            {
                ModelState.AddModelError("", "This email is already registered.");
                return View(model);
            }

            // Check if LRN already exists
            if (await _context.Students.AnyAsync(s => s.LRN == model.LRN))
            {
                ModelState.AddModelError("", "This LRN is already registered.");
                return View(model);
            }

            var student = new Student
            {
                Email = model.Email,
                FirstName = model.FirstName,
                MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName.Trim(),
                LastName = model.LastName,
                LRN = model.LRN,
                GradeLevel = model.GradeLevel,
                Strand = model.Strand,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                CreatedAt = DateTime.Now
            };

            _context.Students.Add(student);
            await _context.SaveChangesAsync(); // async

            // Clear session
            HttpContext.Session.Remove("VerificationCode");
            HttpContext.Session.Remove("VerificationEmail");

            TempData["Success"] = "Registration successful! Please login.";
            return RedirectToAction("Login");
        }
    }
}