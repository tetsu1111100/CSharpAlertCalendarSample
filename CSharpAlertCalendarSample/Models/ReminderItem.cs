
using System.ComponentModel.DataAnnotations;

namespace CSharpAlertCalendarSample.Models
{
    /// <summary>
    /// 代表一個行事曆提醒事項。
    /// </summary>
    public class ReminderItem : IComparable<ReminderItem>
    {
        // 為了實現行事曆項目事後修改/刪除，需要一個唯一識別碼
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Title { get; set; } = string.Empty;

        // 提醒觸發的精確時間 (DateTimeOffset 具備時區資訊，更安全)
        public DateTimeOffset DueTime { get; set; }

        [EmailAddress]
        public string CreatorEmail { get; set; } = string.Empty;

        public ReminderItem(string title, DateTimeOffset dueTime, string creatorEmail)
        {
            Title = title;
            DueTime = dueTime;
            CreatorEmail = creatorEmail;
        }

        /// <summary>
        /// 實現 IComparable 介面，用於 PriorityQueue 排序。
        /// DueTime 越早，優先級越高。
        /// </summary>
        public int CompareTo(ReminderItem? other)
        {
            // 如果 other 為 null，則當前物件優先級高
            if (other is null) return 1;

            // 按 DueTime 升序排序 (越早的越優先)
            return DueTime.CompareTo(other.DueTime);
        }
    }
}
