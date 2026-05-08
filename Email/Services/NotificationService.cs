
using TaskManagement.Data;
using TaskManagement.Models;

namespace TaskManagement.Services
{
    public class NotificationService
    {
        private readonly AccountDbContext _context;
        private static DateTime PhTime =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

        public NotificationService(AccountDbContext context)
        {
            _context = context;
        }

        public async Task NotifyAsync(int accountId, string message, int? projectId = null, int? taskId = null)
        {
            _context.Notifications.Add(new Notification
            {
                AccountId = accountId,
                Message = message,
                ProjectId = projectId,
                TaskId = taskId,
                IsRead = false,
                CreatedAt = PhTime
            });
        }
    }
}