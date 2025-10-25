
using CSharpAlertCalendarSample.Interfaces;
using Microsoft.Extensions.Logging;

namespace CSharpAlertCalendarSample.Services
{
    /// <summary>
    /// 模擬郵件服務，在實際應用中應使用 SmtpClient 或專業的郵件服務。
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public async Task SendReminderEmailAsync(string recipient, string subject, string body)
        {
            // 模擬網路延遲
            await Task.Delay(100);

            _logger.LogInformation(
                "[{Time}] 📧 提醒郵件已發送! 收件人: {Recipient}, 主旨: {Subject}",
                DateTimeOffset.Now.ToString("HH:mm:ss"), recipient, subject);
        }
    }
}
