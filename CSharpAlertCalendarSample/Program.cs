using CSharpAlertCalendarSample.Interfaces;
using CSharpAlertCalendarSample.Models;
using CSharpAlertCalendarSample.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 設置時區為台灣 (CST/UTC+8)
var taiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");

var builder = Host.CreateApplicationBuilder(args);

// 註冊服務
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddSingleton<IReminderService, ReminderScheduler>();
builder.Services.AddHostedService<ReminderScheduler>();

var host = builder.Build();

// 啟動主機
await host.StartAsync();

// 取得服務實例
var reminderService = host.Services.GetRequiredService<IReminderService>();

// --- 測試情境 ---

// 1. 新增三個不同的提醒事項 (測試隨時新增/高可讀性)
var now = DateTimeOffset.Now;

// 1. 立即提醒 (已過期/時間已到)
var item1 = new ReminderItem("立即執行會議", now.AddSeconds(-5), "user1@example.com");
reminderService.AddOrUpdateReminder(item1);

// 2. 正常排程 (30 秒後提醒)
var item2 = new ReminderItem("季度報告會議", now.AddSeconds(30), "user2@example.com");
reminderService.AddOrUpdateReminder(item2);

// 3. 較晚排程 (60 秒後提醒)
var item3 = new ReminderItem("年度預算審核", now.AddSeconds(60), "user3@example.com");
reminderService.AddOrUpdateReminder(item3);

// 4. 【需求 3】事後修改：修改 item2 的時間到更早 (10 秒後)
Console.WriteLine("--- 10 秒後，動態修改 item3 的時間，測試重新排程 ---");
await Task.Delay(TimeSpan.FromSeconds(10));
var modifiedItem3 = new ReminderItem(item3.Title, now.AddSeconds(15), item3.CreatorEmail)
{
    Id = item3.Id // 保持相同的 Id
};
reminderService.AddOrUpdateReminder(modifiedItem3); // 這將會中斷當前 Task.Delay，並將 modifiedItem3 提前

// 5. 【需求 1】隨時新增：新增一個更早的提醒 (5 秒後)
Console.WriteLine("--- 5 秒後，新增一個更早的提醒 ---");
await Task.Delay(TimeSpan.FromSeconds(5));
var item4 = new ReminderItem("緊急測試", now.AddSeconds(12), "user4@example.com");
reminderService.AddOrUpdateReminder(item4); // 這將會再次中斷當前 Task.Delay

// 讓應用程式運行一段時間
Console.WriteLine("--- 應用程式運行 70 秒後停止 ---");
await Task.Delay(TimeSpan.FromSeconds(70));

await host.StopAsync();

