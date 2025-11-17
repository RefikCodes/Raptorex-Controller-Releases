using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CncControlApp.Managers
{
    /// <summary>
    /// Memory & Log Manager - Memory yönetimi ve log filtreleme
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class MemoryLogManager : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        // Constants
        private const int MAX_LOG_COUNT = 1000;
        private const int LOG_CLEANUP_BATCH_SIZE = 100;
        private const int MEMORY_CHECK_INTERVAL_SECONDS = 30;
        private const long HIGH_MEMORY_THRESHOLD = 50_000_000; // 50MB
        private const long CRITICAL_MEMORY_THRESHOLD = 100_000_000; // 100MB

        // Log management
        private readonly ObservableCollection<string> _logMessages;
        private readonly System.Collections.Generic.Queue<string> _logQueue;
        private volatile int _logCount = 0;

        // Memory monitoring
        private long _totalMemoryUsage = 0;
        private DateTime _lastMemoryCheck = DateTime.MinValue;

        // Delegates
        private readonly Action<string> _externalLogDelegate;

        #endregion

        #region Properties

        /// <summary>
        /// Log mesajları koleksiyonu
        /// </summary>
        public ObservableCollection<string> LogMessages => _logMessages;

        /// <summary>
        /// Aktif log sayısı
        /// </summary>
        public int LogCount => _logCount;

        /// <summary>
        /// Tahmini memory kullanımı (bytes)
        /// </summary>
        public long EstimatedMemoryUsage => Interlocked.Read(ref _totalMemoryUsage);

        /// <summary>
        /// Tahmini memory kullanımı (KB)
        /// </summary>
        public double EstimatedMemoryUsageKB => EstimatedMemoryUsage / 1024.0;

        /// <summary>
        /// Tahmini memory kullanımı (MB)
        /// </summary>
        public double EstimatedMemoryUsageMB => EstimatedMemoryUsage / 1024.0 / 1024.0;

        /// <summary>
        /// System memory kullanımı (MB)
        /// </summary>
        public double SystemMemoryUsageMB => GC.GetTotalMemory(false) / 1024.0 / 1024.0;

        /// <summary>
        /// Memory durumu kritik mi?
        /// </summary>
        public bool IsMemoryUsageCritical => GC.GetTotalMemory(false) > CRITICAL_MEMORY_THRESHOLD;

        /// <summary>
        /// Memory durumu yüksek mi?
        /// </summary>
        public bool IsMemoryUsageHigh => GC.GetTotalMemory(false) > HIGH_MEMORY_THRESHOLD;

        #endregion

        #region Events

        /// <summary>
        /// Memory durumu kritik seviyeye ulaştığında tetiklenir
        /// </summary>
        public event EventHandler MemoryUsageCritical;

        /// <summary>
        /// Memory cleanup tamamlandığında tetiklenir
        /// </summary>
        public event EventHandler<int> MemoryCleanupCompleted;

        #endregion

        #region Constructor

        /// <summary>
        /// MemoryLogManager constructor
        /// </summary>
        /// <param name="logMessages">Log mesajları koleksiyonu (UI'dan geliyor)</param>
        /// <param name="externalLogDelegate">Dış log sistemi için delegate (ErrorHandlingService için)</param>
        public MemoryLogManager(ObservableCollection<string> logMessages, Action<string> externalLogDelegate = null)
        {
            _logMessages = logMessages ?? throw new ArgumentNullException(nameof(logMessages));
            _externalLogDelegate = externalLogDelegate;
            _logQueue = new System.Collections.Generic.Queue<string>(MAX_LOG_COUNT);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Log mesajı ekle
        /// </summary>
        /// <param name="message">Eklenecek mesaj</param>
        public void AddLogMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // ✅ FILTER OUT UNNECESSARY LOGS - Keep only important messages
            if (!ShouldDisplayLog(message)) return;

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            int currentLogCount = Interlocked.Increment(ref _logCount);

                            // ✅ Auto cleanup when limit exceeded
                            if (currentLogCount > MAX_LOG_COUNT)
                            {
                                PerformLogCleanup(LOG_CLEANUP_BATCH_SIZE);
                            }

                            // ✅ Add to collections
                            _logMessages.Add(message);
                            _logQueue.Enqueue(message);

                            // ✅ Track memory usage
                            long memoryIncrement = message.Length * 2; // Unicode characters
                            Interlocked.Add(ref _totalMemoryUsage, memoryIncrement);

                            // ✅ External log delegate (for ErrorHandlingService)
                            _externalLogDelegate?.Invoke(message);

                            // ✅ Periodic memory check
                            if (DateTime.Now.Subtract(_lastMemoryCheck).TotalSeconds > MEMORY_CHECK_INTERVAL_SECONDS)
                            {
                                CheckMemoryUsage();
                                _lastMemoryCheck = DateTime.Now;
                            }

                            // ✅ Notify property changes
                            OnPropertyChanged(nameof(LogCount));
                            OnPropertyChanged(nameof(EstimatedMemoryUsage));
                            OnPropertyChanged(nameof(EstimatedMemoryUsageKB));
                            OnPropertyChanged(nameof(EstimatedMemoryUsageMB));
                        }
                        catch (Exception logEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"AddLogMessage inner error: {logEx.Message}");
                        }
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddLogMessage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Manuel log cleanup
        /// </summary>
        /// <param name="itemsToRemove">Silinecek item sayısı (opsiyonel)</param>
        /// <returns>Silinen item sayısı</returns>
        public int ClearOldLogs(int? itemsToRemove = null)
        {
            try
            {
                int removeCount = itemsToRemove ?? LOG_CLEANUP_BATCH_SIZE;
                long freedMemory = PerformLogCleanup(removeCount);
                return removeCount; // Return the number of items removed, not the memory freed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearOldLogs error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Tüm logları temizle
        /// </summary>
        public void ClearAllLogs()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    int clearedCount = _logMessages.Count;
                    
                    _logMessages.Clear();
                    _logQueue.Clear();
                    
                    Interlocked.Exchange(ref _logCount, 0);
                    Interlocked.Exchange(ref _totalMemoryUsage, 0);
                    
                    System.Diagnostics.Debug.WriteLine($"All logs cleared: {clearedCount} items");
                    
                    OnPropertyChanged(nameof(LogCount));
                    OnPropertyChanged(nameof(EstimatedMemoryUsage));
                    OnPropertyChanged(nameof(EstimatedMemoryUsageKB));
                    OnPropertyChanged(nameof(EstimatedMemoryUsageMB));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearAllLogs error: {ex.Message}");
            }
        }

        /// <summary>
        /// Memory durumunu kontrol et
        /// </summary>
        public void CheckMemoryUsage()
        {
            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                var estimatedLogMemory = Interlocked.Read(ref _totalMemoryUsage);

                bool wasMemoryHigh = IsMemoryUsageHigh;
                bool wasMemoryCritical = IsMemoryUsageCritical;

                OnPropertyChanged(nameof(SystemMemoryUsageMB));
                OnPropertyChanged(nameof(IsMemoryUsageHigh));
                OnPropertyChanged(nameof(IsMemoryUsageCritical));

                if (currentMemory > HIGH_MEMORY_THRESHOLD)
                {
                    System.Diagnostics.Debug.WriteLine($"High memory usage detected: {currentMemory / 1024 / 1024}MB (Estimated log memory: {estimatedLogMemory / 1024 / 1024}MB)");

                    if (currentMemory > CRITICAL_MEMORY_THRESHOLD)
                    {
                        if (!wasMemoryCritical)
                        {
                            MemoryUsageCritical?.Invoke(this, EventArgs.Empty);
                        }
                        
                        ForceMemoryCleanup();
                    }

                    // ✅ Trigger garbage collection for high memory usage
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Memory check error: {ex.Message}");
            }
        }

        /// <summary>
        /// Zorla memory cleanup
        /// </summary>
        /// <returns>Temizlenen memory miktarı (KB)</returns>
        public long ForceMemoryCleanup()
        {
            try
            {
                long freedMemory = 0;
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    int itemsToRemove = (int)Math.Min(MAX_LOG_COUNT / 2, _logMessages.Count);
                    freedMemory = PerformLogCleanup(itemsToRemove);
                    
                    System.Diagnostics.Debug.WriteLine($"Emergency cleanup completed. Remaining logs: {_logMessages.Count}, Freed memory: {freedMemory / 1024}KB");
                });

                // ✅ Force garbage collection
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced);

                return freedMemory / 1024; // Return KB
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Force cleanup error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Memory status raporu al
        /// </summary>
        /// <returns>Memory status bilgisi</returns>
        public string GetMemoryStatusReport()
        {
            try
            {
                var systemMemory = GC.GetTotalMemory(false);
                var logMemoryEstimate = Interlocked.Read(ref _totalMemoryUsage);
                var logCount = Interlocked.CompareExchange(ref _logCount, 0, 0);

                return $"Memory Status - System: {systemMemory / 1024 / 1024}MB, " +
                       $"Log Estimate: {logMemoryEstimate / 1024}KB, " +
                       $"Log Count: {logCount:N0}, " +
                       $"Queue Size: {_logQueue.Count}";
            }
            catch (Exception ex)
            {
                return $"Memory status error: {ex.Message}";
            }
        }

        /// <summary>
        /// Debug için memory status yazdır
        /// </summary>
        public void PrintMemoryStatus()
        {
            try
            {
                string status = GetMemoryStatusReport();
                System.Diagnostics.Debug.WriteLine(status);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrintMemoryStatus error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Log cleanup işlemini gerçekleştir
        /// </summary>
        /// <param name="itemsToRemove">Silinecek item sayısı</param>
        /// <returns>Silinen memory miktarı (bytes)</returns>
        private long PerformLogCleanup(int itemsToRemove)
        {
            long memoryFreed = 0;
            int actualItemsRemoved = Math.Min(itemsToRemove, _logMessages.Count);

            for (int i = 0; i < actualItemsRemoved; i++)
            {
                if (_logMessages.Count > 0)
                {
                    string removedMessage = _logMessages[0];
                    _logMessages.RemoveAt(0);

                    if (_logQueue.Count > 0)
                        _logQueue.Dequeue();

                    memoryFreed += removedMessage.Length * 2; // Unicode characters
                }
            }

            // ✅ Update counters
            Interlocked.Exchange(ref _logCount, _logMessages.Count);
            Interlocked.Add(ref _totalMemoryUsage, -memoryFreed);

            // ✅ Ensure memory usage doesn't go negative
            if (Interlocked.Read(ref _totalMemoryUsage) < 0)
            {
                Interlocked.Exchange(ref _totalMemoryUsage, 0);
            }

            // ✅ Notify cleanup completed
            MemoryCleanupCompleted?.Invoke(this, actualItemsRemoved);

            // ✅ Update properties
            OnPropertyChanged(nameof(LogCount));
            OnPropertyChanged(nameof(EstimatedMemoryUsage));
            OnPropertyChanged(nameof(EstimatedMemoryUsageKB));
            OnPropertyChanged(nameof(EstimatedMemoryUsageMB));

            return memoryFreed;
        }

        /// <summary>
        /// Determine if a log message should be displayed based on importance filter
        /// </summary>
        private bool ShouldDisplayLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            string lower = message.ToLowerInvariant();

            // ✅ ALWAYS SHOW: Connection events
            if (message.Contains("BAĞLANTI KURULDU") || message.Contains("BAĞLANTI KESİLDİ"))
                return true;

            // ✅ ALWAYS SHOW: Critical errors and alarms
            if (lower.Contains("error") || lower.Contains("hata") || lower.Contains("alarm"))
                return true;

            // ❌ HIDE EVERYTHING ELSE
            return false;
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Property changed event
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Property changed notification
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Resources'ları temizle
        /// </summary>
        public void Dispose()
        {
            try
            {
                ClearAllLogs();
                System.Diagnostics.Debug.WriteLine("MemoryLogManager disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MemoryLogManager dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}