using Microsoft.EntityFrameworkCore;
using PataoSmartQueuing.Models;

namespace PataoSmartQueuing.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ==========================
        // DATABASE TABLES
        // ==========================

        public DbSet<Student> Students { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<AdminSettings> AdminSettings { get; set; }
        public DbSet<Queue> Queues { get; set; }
        public DbSet<QueueStudent> QueueStudents { get; set; }
        public DbSet<PushSubscription> PushSubscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================
            // ADMIN -> QUEUE
            // ==========================

            modelBuilder.Entity<Queue>()
                .HasOne(q => q.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(q => q.CreatedByAdminID)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================
            // QUEUE -> QUEUESTUDENT
            // ==========================

            modelBuilder.Entity<QueueStudent>()
                .HasOne(qs => qs.Queue)
                .WithMany(q => q.QueueStudents)
                .HasForeignKey(qs => qs.QueueID)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================
            // STUDENT -> QUEUESTUDENT
            // ==========================

            modelBuilder.Entity<QueueStudent>()
                .HasOne(qs => qs.Student)
                .WithMany(s => s.QueueStudents)
                .HasForeignKey(qs => qs.StudentID)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================
            // STUDENT -> PUSHSUBSCRIPTION
            // ==========================

            modelBuilder.Entity<PushSubscription>()
                .HasOne(ps => ps.Student)
                .WithMany(s => s.PushSubscriptions)
                .HasForeignKey(ps => ps.StudentID)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================
            // UNIQUE CONSTRAINTS
            // ==========================

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.Email)
                .IsUnique();

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.LRN)
                .IsUnique();

            modelBuilder.Entity<Admin>()
                .HasIndex(a => a.Email)
                .IsUnique();

            modelBuilder.Entity<Queue>()
                .HasIndex(q => q.QueueName)
                .IsUnique();

            modelBuilder.Entity<Queue>()
                .HasIndex(q => q.QueueCode)
                .IsUnique();

            // ==========================
            // COLUMN CONFIGURATIONS
            // ==========================

            modelBuilder.Entity<Student>()
                .Property(s => s.Email)
                .HasMaxLength(255);

            modelBuilder.Entity<Student>()
                .Property(s => s.FirstName)
                .HasMaxLength(50);

            modelBuilder.Entity<Student>()
                .Property(s => s.MiddleName)
                .HasMaxLength(50);

            modelBuilder.Entity<Student>()
                .Property(s => s.LastName)
                .HasMaxLength(50);

            modelBuilder.Entity<Student>()
                .Property(s => s.LRN)
                .HasMaxLength(12);

            modelBuilder.Entity<Student>()
                .Property(s => s.ProfilePhoto)
                .HasMaxLength(255);

            modelBuilder.Entity<Admin>()
                .Property(a => a.Email)
                .HasMaxLength(255);

            modelBuilder.Entity<Admin>()
                .Property(a => a.FirstName)
                .HasMaxLength(100);

            modelBuilder.Entity<Admin>()
                .Property(a => a.MiddleName)
                .HasMaxLength(100);

            modelBuilder.Entity<Admin>()
                .Property(a => a.LastName)
                .HasMaxLength(100);

            modelBuilder.Entity<Admin>()
                .Property(a => a.Role)
                .HasMaxLength(50);

            modelBuilder.Entity<Queue>()
                .Property(q => q.QueueName)
                .HasMaxLength(100);

            modelBuilder.Entity<Queue>()
                .Property(q => q.QueueCode)
                .HasMaxLength(50);

            modelBuilder.Entity<PushSubscription>()
                .Property(ps => ps.Endpoint)
                .HasMaxLength(500);

            modelBuilder.Entity<PushSubscription>()
                .Property(ps => ps.P256dh)
                .HasMaxLength(200);

            modelBuilder.Entity<PushSubscription>()
                .Property(ps => ps.Auth)
                .HasMaxLength(200);
        }
    }
}