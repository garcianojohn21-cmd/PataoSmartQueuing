using Microsoft.AspNetCore.Mvc;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Models;
using Microsoft.EntityFrameworkCore;

namespace PataoSmartQueuing.Controllers
{
    public class AdminAccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminAccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Admin Login Page
        [HttpGet]
        public IActionResult Login(string token)
        {
            // ✅ Token protection
            var portalToken = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetSection("AdminSettings")["PortalToken"];

            if (token != portalToken)
            {
                return Unauthorized("Invalid access portal");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Email == email);
            if (admin == null || !BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid credentials");
                return View();
            }

            HttpContext.Session.SetInt32("AdminID", admin.AdminID);
            HttpContext.Session.SetString("AdminRole", admin.Role);
            HttpContext.Session.SetString("AdminName", admin.FirstName);

            return RedirectToAction("Dashboard", "Admin");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "AdminAccount", new { token = "SECRET123" });
        }
    }
}