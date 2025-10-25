
using System.Collections.Concurrent;
using CSharpAlertCalendarSample.Interfaces;
using CSharpAlertCalendarSample.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CSharpAlertCalendarSample.Services
{
    /// <summary>
    /// 核心提醒排程器，以 IHostedService 形式在背景穩定運行。
    /// 採用生產者-消費者模式：AddOrUpdate 是生產者，ExecuteAsync 是消費者。
    /// </summary>
    public class ReminderScheduler : BackgroundService, IReminderService
    {
        private readonly IEmailService _emailService;
        private readonly ILogger<ReminderScheduler> _logger;

        // 多執行緒安全的字典，用於快速查找和修改事項 (用於需求 3：事後修改)
        private readonly ConcurrentDictionary<Guid, ReminderItem> _reminderMap = new();

        // 核心資料結構：PriorityQueue，自動按 DueTime 排序 (用於需求 4：高效能)
        private readonly PriorityQueue<ReminderItem, DateTimeOffset> _priorityQueue = new();

        // 鎖定對象，確保對佇列和字典的操作是原子性的 (用於需求 4：安全性)
        private readonly object _lock = new();

        // 觀察者模式的核心：用於通知 ExecuteAsync 循環重新計算延遲時間 (用於需求 1, 3：隨時新增/修改)
        private CancellationTokenSource _delayCts = new();

        public ReminderScheduler(IEmailService emailService, ILogger<ReminderScheduler> logger)
        {
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>
        /// 【生產者】新增或修改提醒事項
        /// </summary>
        public void AddOrUpdateReminder(ReminderItem item)
        {
            lock (_lock)
            {
                // 1. 如果是修改 (Id 存在於 Map 中)
                if (_reminderMap.ContainsKey(item.Id))
                {
                    // **移除舊項目**
                    // 由於 PriorityQueue 缺乏高效的 Find/Remove 機制，簡單的做法是
                    // 標記舊項目為「已處理」並加入新項目。
                    // 但為了精確性，我們必須確保舊項目不會被觸發。

                    // 實際企業級應用會用更複雜的機制 (如資料庫，或像 Hangfire 那樣有 Job Key) 
                    // 但在這個範例中，為了實作精準的 In-Memory 機制:
                    // 我們直接替換 Map 中的項目，並在佇列中重新加入新項目。

                    // 為了避免複雜的重新構建佇列，我們依賴 ExecuteAsync 中的檢查來過濾掉舊的、已修改的項目。
                    // 這裡只需更新 Map 並加入新項目。
                }

                // 2. 更新 Map
                _reminderMap[item.Id] = item;

                // 3. 將項目加入佇列
                _priorityQueue.Enqueue(item, item.DueTime);

                _logger.LogInformation(
                    "新增/更新提醒: {Title}，時間: {DueTime}",
                    item.Title, item.DueTime.ToString("yyyy-MM-dd HH:mm:ss"));

                // 4. 【觀察者模式】取消當前的 Task.Delay，強制排程器重新計算等待時間
                CancelCurrentDelay();
            }
        }

        /// <summary>
        /// 【消費者】執行背景排程循環
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("提醒排程器啟動。");

            while (!stoppingToken.IsCancellationRequested)
            {
                ReminderItem? nextItem = null;
                TimeSpan delayTime = Timeout.InfiniteTimeSpan;

                // 1. 鎖定並取得下一個最近的提醒事項
                lock (_lock)
                {
                    while (_priorityQueue.Count > 0)
                    {
                        var peekedItem = _priorityQueue.Peek();

                        // 檢查 Peek 的項目是否是最新版本
                        if (_reminderMap.TryGetValue(peekedItem.Id, out var currentItem) && currentItem.Equals(peekedItem))
                        {
                            // 是最新版本，使用它
                            nextItem = _priorityQueue.Dequeue();
                            break;
                        }
                        else
                        {
                            // 這是已修改或已刪除的舊版本，直接丟棄
                            _priorityQueue.Dequeue();
                        }
                    }

                    if (nextItem != null)
                    {
                        delayTime = nextItem.DueTime - DateTimeOffset.Now;

                        // 重新創建 CancellationTokenSource，準備新的延遲等待
                        _delayCts = new CancellationTokenSource();
                    }
                }

                // 2. 執行等待 (Task.Delay)
                if (nextItem != null && delayTime > TimeSpan.Zero)
                {
                    _logger.LogInformation("設定 Task.Delay: 等待 {DelayTime} 後觸發 {Title}",
                                            delayTime.TotalSeconds.ToString("F1") + "秒", nextItem.Title);

                    try
                    {
                        // 使用 _delayCts.Token 讓等待可以被 AddOrUpdateReminder 提前中斷
                        await Task.Delay(delayTime, _delayCts.Token);
                    }
                    catch (TaskCanceledException) when (_delayCts.IsCancellationRequested)
                    {
                        // 被 AddOrUpdateReminder 提前取消 (有新的更早的事件)
                        _logger.LogWarning("Task.Delay 被中斷。重新排程。");
                        // 必須將當前項目（nextItem）重新放回佇列中，因為它還沒被觸發
                        lock (_lock)
                        {
                            _priorityQueue.Enqueue(nextItem, nextItem.DueTime);
                        }
                        continue; // 跳到下一個循環，重新計算最短 Delay
                    }
                    catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // 應用程式關閉，跳出循環
                        break;
                    }
                    // Time's up! 延遲結束，觸發提醒
                }
                else if (nextItem != null && delayTime <= TimeSpan.Zero)
                {
                    // 時間已經到了 (或已經過期)，立即觸發
                    _logger.LogWarning("事件 {Title} 已過期或時間已到，立即執行。", nextItem.Title);
                }
                else
                {
                    // 佇列為空，等待新的提醒加入
                    try
                    {
                        _logger.LogInformation("佇列為空，進入無限等待...");
                        // 等待直到被 AddOrUpdateReminder 喚醒 (Timeout.Infinite) 或服務關閉
                        await Task.Delay(Timeout.Infinite, _delayCts.Token);
                    }
                    catch (TaskCanceledException) when (_delayCts.IsCancellationRequested)
                    {
                        // 被 AddOrUpdateReminder 喚醒
                        _logger.LogWarning("無限等待被中斷。重新檢查佇列。");
                        continue;
                    }
                    catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                // 3. 觸發提醒動作 (發送郵件) (需求 2)
                if (nextItem != null)
                {
                    // 執行動作
                    await _emailService.SendReminderEmailAsync(
                        nextItem.CreatorEmail,
                        $"提醒: {nextItem.Title}",
                        $"您的行事曆事項 '{nextItem.Title}' 提醒時間已到。"
                    );

                    // 動作完成後，從 Map 中移除，確保不再被處理
                    lock (_lock)
                    {
                        _reminderMap.TryRemove(nextItem.Id, out _);
                    }
                }
            }








        }

        /// <summary>
        /// 輔助方法：安全地取消當前的 Task.Delay
        /// </summary>
        private void CancelCurrentDelay()
        {
            lock (_lock)
            {
                if (!_delayCts.IsCancellationRequested)
                {
                    // 取消當前正在等待的 Task.Delay
                    _delayCts.Cancel();
                }
            }
        }
    }
}
