using Microsoft.EntityFrameworkCore.Migrations;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;

// Models/Notification.cs
namespace TaskManagement.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int AccountId { get; set; }          // who receives it
        public int? ProjectId { get; set; }
        public Project? Project { get; set; }
        public int? TaskId { get; set; }
        public TaskItem? TaskEntity { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; }

        public Account Account { get; set; }
    }
}
