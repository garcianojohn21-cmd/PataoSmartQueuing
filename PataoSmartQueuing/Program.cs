using Microsoft.EntityFrameworkCore;
using PataoSmartQueuing.Data;
using PataoSmartQueuing.Hubs;
using PataoSmartQueuing.Models;
using PataoSmartQueuing.Services;


// ADD THIS
AppContext.SetSwitch(
    "Npgsql.EnableLegacyTimestampBehavior",
    true);

var builder = WebApplication.CreateBuilder(args);

// ===========================
// SERVICES
// ===========================

builder.Services.AddControllersWithViews();

// PostgreSQL (Neon)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PasswordChangeEmailService>();

builder.Services.AddHostedService<QueueTimeoutService>();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<AdminSettings>(
    builder.Configuration.GetSection("AdminSettings"));

builder.Services.AddSignalR();

var app = builder.Build();

// ===========================
// DATABASE MIGRATION + SEED
// ===========================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Automatically apply migrations on startup
    db.Database.Migrate();

    // Seed Super Admin
    if (!db.Admins.Any())
    {
        db.Admins.Add(new Admin
        {
            Email = "superadmin@patao.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("SuperSecure123"),
            FirstName = "Main",
            LastName = "Admin",
            Role = "SuperAdmin",
            CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }

    // Seed Portal Token
    if (!db.AdminSettings.Any())
    {
        db.AdminSettings.Add(new AdminSettings
        {
            PortalToken = "SECRET123",
            UpdatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }
}

// ===========================
// MIDDLEWARE
// ===========================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// Session before Authentication
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// ===========================
// ADMIN ACCESS PROTECTION
// ===========================

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower();

    if (!string.IsNullOrEmpty(path) &&
        path.StartsWith("/admin"))
    {
        // Prevent logged-in students from accessing admin pages
        if (context.Session.GetInt32("StudentID") != null)
        {
            context.Response.Redirect("/");
            return;
        }

        // Validate admin portal token
        if (path.Contains("/admin/login") &&
            context.Request.Method == "GET")
        {
            var token = context.Request.Query["token"].ToString();

            using var scope = app.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var portalToken = db.AdminSettings
                .Select(x => x.PortalToken)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(token) ||
                token != portalToken)
            {
                context.Response.StatusCode = 401;

                await context.Response.WriteAsync(
                    "Access denied. Invalid portal token.");

                return;
            }
        }
    }

    await next();
});

// ===========================
// ROUTES
// ===========================

app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{action=Login}/{id?}",
    defaults: new { controller = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ===========================
// SIGNALR
// ===========================

app.MapHub<NotificationHub>("/notificationHub");

app.MapControllers();

#if DEBUG
PataoSmartQueuing.Tools.VapidKeyGenerator.GenerateKeys();
#endif

app.Run();