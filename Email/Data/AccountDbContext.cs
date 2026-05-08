
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics;
using TaskManagement.Models;
namespace TaskManagement.Data
{
    public class AccountDbContext : DbContext
    {
        public AccountDbContext(DbContextOptions<AccountDbContext> options) : base(options) { }
        public DbSet<Account> Accounts { get; set; }
        // Task Management
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<TaskAssignment> TaskAssignments { get; set; }
        public DbSet<TaskComment> TaskComments { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        // Project Management
        public DbSet<Project> Projects { get; set; }            
        public DbSet<ProjectMember> ProjectMembers { get; set; }
        public DbSet<TaskItemStatus> TaskStatuses { get; set; }
        public DbSet<TaskPriority> TaskPriorities { get; set; }
        public DbSet<ProjectStatus> ProjectStatuses { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
        public DbSet<StickyNote> StickyNotes { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var hasher = new PasswordHasher<Account>();
            var admin = new Account
            {
                Id = 1,
                Name = "Admin",
                Email = "admin@admin.com",
                Role = "Admin",
                Specialization = string.Empty,
                isActive = true,
                CreatedAt = new DateTime(2026, 1, 1),
                UpdatedAt = new DateTime(2026, 1, 1)
            };
            admin.PasswordHash = hasher.HashPassword(admin, "Admin@123");
            modelBuilder.Entity<Account>().HasData(admin);
            // Task relationships
            modelBuilder.Entity<TaskItem>()
                .HasIndex(t => new { t.ProjectId, t.ParentTaskId, t.IsDeleted });
            modelBuilder.Entity<TaskItem>()
                .HasIndex(t => new { t.ProjectId, t.StatusId, t.IsDeleted });
            modelBuilder.Entity<TaskItem>()
                .HasIndex(t => new { t.ProjectId, t.DueDate, t.IsDeleted });
            modelBuilder.Entity<TaskItem>()
                .HasIndex(t => new { t.ParentTaskId, t.IsDeleted });

            modelBuilder.Entity<TaskAssignment>()
                .HasIndex(a => new { a.TaskId, a.AccountId, a.IsDeleted });
            modelBuilder.Entity<TaskAssignment>()
                .HasIndex(a => new { a.AccountId, a.IsDeleted });

            modelBuilder.Entity<TaskComment>()
                .HasIndex(c => new { c.TaskId, c.IsDeleted, c.CreatedAt });

            modelBuilder.Entity<ProjectMember>()
                .HasIndex(m => new { m.ProjectId, m.AccountId, m.IsDeleted });

            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.IsDeleted, p.CreatedAt });

            modelBuilder.Entity<TaskAssignment>()
                .HasOne<TaskItem>(a => a.Task)
                .WithMany(t => t.Assignments)
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<TaskComment>()
                .HasOne<TaskItem>()
                .WithMany(t => t.Comments)
                .HasForeignKey(c => c.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditLog>()
                .HasOne<TaskItem>()
                .WithMany(t => t.AuditLogs)
                .HasForeignKey(l => l.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // TaskItem self-referencing for subtasks
            modelBuilder.Entity<TaskItem>()
                .HasOne<TaskItem>()
                .WithMany(t => t.SubTasks)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.Restrict);

            // Project → Tasks
            modelBuilder.Entity<TaskItem>()
                .HasOne(t => t.Project)       
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Project → Members
            modelBuilder.Entity<ProjectMember>()
                .HasOne<Project>()
                .WithMany(p => p.Members)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
               .HasOne(p => p.CreatedBy)
               .WithMany()
               .HasForeignKey(p => p.CreatedById)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasOne<Account>()
                .WithMany()
                .HasForeignKey(nameof(Project.ProjectManagerId))
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasOne<Account>()
                .WithMany()
                .HasForeignKey(nameof(Project.ScrumMasterId))
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.TaskEntity)
                .WithMany()
                .HasForeignKey(n => n.TaskId)
                .OnDelete(DeleteBehavior.NoAction);

            // TaskItem - TaskStatus 
            modelBuilder.Entity<TaskItem>()
                .HasOne(t => t.Status)
                .WithMany()
                .HasForeignKey(t => t.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            // TaskItem - TaskPriority 
            modelBuilder.Entity<TaskItem>()
                .HasOne(t => t.Priority)
                .WithMany()
                .HasForeignKey(t => t.PriorityId)
                .OnDelete(DeleteBehavior.Restrict);

            // Project - ProjectStatus 
            modelBuilder.Entity<Project>()
                .HasOne(p => p.Status)
                .WithMany()
                .HasForeignKey(p => p.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            //  TaskStatus 
            modelBuilder.Entity<TaskItemStatus>().HasData(
                new TaskItemStatus { Id = 1, Name = "Not Started", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskItemStatus { Id = 2, Name = "In Progress", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskItemStatus { Id = 3, Name = "For Review", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskItemStatus { Id = 4, Name = "Completed", CreatedAt = new DateTime(2026, 1, 1) }
            );

            //  TaskPriority 
            modelBuilder.Entity<TaskPriority>().HasData(
                new TaskPriority { Id = 1, Name = "Urgent", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskPriority { Id = 2, Name = "Important", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskPriority { Id = 3, Name = "Medium", CreatedAt = new DateTime(2026, 1, 1) },
                new TaskPriority { Id = 4, Name = "Low", CreatedAt = new DateTime(2026, 1, 1) }
            );

            // ProjectStatus 
            modelBuilder.Entity<ProjectStatus>().HasData(
                new ProjectStatus { Id = 1, Name = "Not Started", CreatedAt = new DateTime(2026, 1, 1) },
                new ProjectStatus { Id = 2, Name = "Active", CreatedAt = new DateTime(2026, 1, 1) },
                new ProjectStatus { Id = 3, Name = "Completed", CreatedAt = new DateTime(2026, 1, 1) }
            );

        }
    }
}
