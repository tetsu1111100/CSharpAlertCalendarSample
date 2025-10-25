
using CSharpAlertCalendarSample.Models;

namespace CSharpAlertCalendarSample.Interfaces
{
    public interface IReminderService
    {
        void AddOrUpdateReminder(ReminderItem item);
    }
}
