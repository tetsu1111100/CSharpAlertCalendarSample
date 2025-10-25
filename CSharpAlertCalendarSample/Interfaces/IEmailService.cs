
namespace CSharpAlertCalendarSample.Interfaces
{
    public interface IEmailService
    {
        Task SendReminderEmailAsync(string recipient, string subject, string body);
    }
}
