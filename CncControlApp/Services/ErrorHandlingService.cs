using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CncControlApp.Services
{
    /// <summary>
    /// Error Handling Service - Error logging, GRBL error processing ve diagnostics
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class ErrorHandlingService : IDisposable
    {
        #region Constants

        private const int MAX_ERROR_HISTORY = 50;
        private const int MAX_ERROR_STATISTICS = 100;

        #endregion

        #region Fields

        // ✅ Error handling için fields
        private readonly object _errorLogLock = new object();
        private readonly Queue<string> _errorHistory = new Queue<string>(MAX_ERROR_HISTORY);
        private readonly ConcurrentDictionary<string, int> _errorStatistics = new ConcurrentDictionary<string, int>();

        // ✅ Delegates for external communication
        private Action<string> _addLogMessageDelegate;

        #endregion

        #region Enums

        /// <summary>
        /// Error severity levels
        /// </summary>
        public enum ErrorSeverity
        {
            Info,
            Warning,
            Error,
            Critical
        }

        #endregion

        #region Custom Exception Types

        /// <summary>
        /// Custom CNC Control exception with enhanced context information
        /// </summary>
        public class CncControlException : Exception
        {
            public ErrorSeverity Severity { get; }
            public string Operation { get; }
            public DateTime Timestamp { get; }

            public CncControlException(string operation, string message, ErrorSeverity severity = ErrorSeverity.Error, Exception innerException = null)
                : base(message, innerException)
            {
                Operation = operation;
                Severity = severity;
                Timestamp = DateTime.Now;
            }
        }

        #endregion

        #region Constructor

        public ErrorHandlingService()
        {
            InitializeErrorHandling();
        }

        public ErrorHandlingService(Action<string> addLogMessageDelegate)
        {
            _addLogMessageDelegate = addLogMessageDelegate;
            InitializeErrorHandling();
        }

        private void InitializeErrorHandling()
        {
            System.Diagnostics.Debug.WriteLine("ErrorHandlingService: Initialized successfully");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Enhanced error logging system - C# 7.3 Compatible
        /// </summary>
        /// <param name="operation">Operation name where error occurred</param>
        /// <param name="ex">Exception object</param>
        /// <param name="severity">Error severity level</param>
        public void LogError(string operation, Exception ex, ErrorSeverity severity = ErrorSeverity.Error)
        {
            try
            {
                var errorMessage = $"{operation}: {ex.Message}";

                lock (_errorLogLock)
                {
                    _errorHistory.Enqueue(errorMessage);
                    if (_errorHistory.Count > MAX_ERROR_HISTORY)
                    {
                        _errorHistory.Dequeue();
                    }
                }

                // ✅ Update error statistics
                UpdateErrorStatistics(operation, severity);

                // ✅ C# 7.3 Compatible - switch expression yerine if-else
                string errorPrefix = GetSeverityPrefix(severity);

                var logMessage = $"{errorPrefix}: [{operation}] {ex.Message}";

                _addLogMessageDelegate?.Invoke(logMessage);
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {logMessage}");
            }
            catch (Exception logEx)
            {
                // ✅ Fallback error logging
                System.Diagnostics.Debug.WriteLine($"LOGGING ERROR: {logEx.Message}");
            }
        }

        /// <summary>
        /// Log error with custom message
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <param name="message">Custom error message</param>
        /// <param name="severity">Error severity</param>
        public void LogError(string operation, string message, ErrorSeverity severity = ErrorSeverity.Error)
        {
            LogError(operation, new Exception(message), severity);
        }

        /// <summary>
        /// Get error history for diagnostics
        /// </summary>
        /// <returns>List of error messages</returns>
        public List<string> GetErrorHistory()
        {
            lock (_errorLogLock)
            {
                return _errorHistory.ToList();
            }
        }

        /// <summary>
        /// Get error statistics by operation and severity
        /// </summary>
        /// <returns>Dictionary of error statistics</returns>
        public Dictionary<string, int> GetErrorStatistics()
        {
            return _errorStatistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Clear error history
        /// </summary>
        public void ClearErrorHistory()
        {
            lock (_errorLogLock)
            {
                _errorHistory.Clear();
                _errorStatistics.Clear();
                _addLogMessageDelegate?.Invoke("> Error history cleared");
            }
        }

        /// <summary>
        /// Enhanced error message processing for GRBL/FluidNC errors
        /// </summary>
        /// <param name="errorMessage">Raw error message from machine</param>
        public void ProcessErrorMessage(string errorMessage)
        {
            try
            {
                // GRBL error pattern: "error:X" veya "GRBL: error:X"
                var errorMatch = Regex.Match(errorMessage, @"error:(\d+)", RegexOptions.IgnoreCase);

                if (errorMatch.Success && int.TryParse(errorMatch.Groups[1].Value, out int errorCode))
                {
                    HandleGrblError(errorCode, errorMessage);
                }
                else
                {
                    // Generic error handling
                    _addLogMessageDelegate?.Invoke($"> ❌ HATA: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessErrorMessage error: {ex.Message}");
                _addLogMessageDelegate?.Invoke($"> ❌ HATA: {errorMessage}");
            }
        }

        /// <summary>
        /// Update delegate for external logging
        /// </summary>
        /// <param name="addLogMessageDelegate">Log message delegate</param>
        public void UpdateLogDelegate(Action<string> addLogMessageDelegate)
        {
            _addLogMessageDelegate = addLogMessageDelegate;
        }

        /// <summary>
        /// Get error count by severity
        /// </summary>
        /// <param name="severity">Error severity level</param>
        /// <returns>Count of errors with specified severity</returns>
        public int GetErrorCount(ErrorSeverity severity)
        {
            var severityPrefix = GetSeverityPrefix(severity);
            return _errorStatistics
                .Where(kvp => kvp.Key.StartsWith(severityPrefix))
                .Sum(kvp => kvp.Value);
        }

        /// <summary>
        /// Get formatted error report - C# 7.3 Compatible
        /// </summary>
        /// <returns>Formatted error report string</returns>
        public string GetErrorReport()
        {
            try
            {
                var report = new System.Text.StringBuilder();
                report.AppendLine("=== ERROR HANDLING REPORT ===");
                report.AppendLine($"Report Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine();

                // Error counts by severity
                report.AppendLine("Error Counts by Severity:");
                report.AppendLine($"  Critical: {GetErrorCount(ErrorSeverity.Critical)}");
                report.AppendLine($"  Error: {GetErrorCount(ErrorSeverity.Error)}");
                report.AppendLine($"  Warning: {GetErrorCount(ErrorSeverity.Warning)}");
                report.AppendLine($"  Info: {GetErrorCount(ErrorSeverity.Info)}");
                report.AppendLine();

                // Recent error history - C# 7.3 Compatible way
                var errorHistory = GetErrorHistory();
                if (errorHistory.Count > 0)
                {
                    report.AppendLine("Recent Error History:");
                    
                    // ✅ C# 7.3 Compatible - TakeLast yerine Skip/Take kullan
                    int skipCount = Math.Max(0, errorHistory.Count - 10);
                    var recentErrors = errorHistory.Skip(skipCount).Take(10);
                    
                    foreach (var error in recentErrors)
                    {
                        report.AppendLine($"  {error}");
                    }
                }
                else
                {
                    report.AppendLine("No errors recorded.");
                }

                return report.ToString();
            }
            catch (Exception ex)
            {
                return $"Error generating report: {ex.Message}";
            }
        }

        /// <summary>
        /// Get recent error history (last N errors) - C# 7.3 Compatible
        /// </summary>
        /// <param name="count">Number of recent errors to return</param>
        /// <returns>List of recent error messages</returns>
        public List<string> GetRecentErrorHistory(int count = 10)
        {
            try
            {
                lock (_errorLogLock)
                {
                    var allErrors = _errorHistory.ToList();
                    
                    // ✅ C# 7.3 Compatible way to get last N items
                    int skipCount = Math.Max(0, allErrors.Count - count);
                    return allErrors.Skip(skipCount).Take(count).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRecentErrorHistory error: {ex.Message}");
                return new List<string>();
            }
        }

        #endregion

        #region GRBL Error Handling System

        // ✅ GRBL Error kodları ve açıklamaları
        private static readonly Dictionary<int, (string Description, string Solution, ErrorSeverity Severity)> GrblErrorCodes =
            new Dictionary<int, (string, string, ErrorSeverity)>
        {
            { 1, ("Expected command letter", "G-Code komutunda harf eksik (örn: G, M, X)", ErrorSeverity.Error) },
            { 2, ("Bad number format", "Sayı formatı hatalı", ErrorSeverity.Error) },
            { 3, ("Invalid statement", "Geçersiz komut - komut harfi bekleniyor", ErrorSeverity.Error) },
            { 4, ("Value < 0", "Negatif değer kabul edilmiyor", ErrorSeverity.Error) },
            { 5, ("Setting disabled", "Bu ayar devre dışı", ErrorSeverity.Warning) },
            { 6, ("Value < 3 usec", "Değer 3 mikrosaniyeden küçük olamaz", ErrorSeverity.Error) },
            { 7, ("EEPROM read fail", "EEPROM okuma hatası", ErrorSeverity.Critical) },
            { 8, ("Not idle", "Makine Idle durumunda değil", ErrorSeverity.Warning) },
            { 9, ("Lock", "Makine kilitli - $X ile kilidi açın", ErrorSeverity.Warning) },
            { 10, ("Soft limit", "Yazılım limiti aşıldı", ErrorSeverity.Error) },
            { 11, ("Overflow", "Komut buffer'ı doldu", ErrorSeverity.Error) },
            { 12, ("Max step rate exceeded", "Maksimum step hızı aşıldı", ErrorSeverity.Error) },
            { 13, ("Check door", "Güvenlik kapısını kontrol edin", ErrorSeverity.Warning) },
            { 14, ("Line length exceeded", "Komut satırı çok uzun", ErrorSeverity.Error) },
            { 15, ("Travel exceeded", "Hareket limiti aşıldı", ErrorSeverity.Error) },
            { 16, ("Invalid jog command", "Geçersiz jog komutu", ErrorSeverity.Error) },
            { 17, ("Setting disabled", "Laser modu ayarı devre dışı", ErrorSeverity.Warning) },
            { 18, ("Unsupported command", "Desteklenmeyen komut", ErrorSeverity.Error) },
            { 19, ("Modal group violation", "Modal grup ihlali", ErrorSeverity.Error) },
            { 20, ("Unsupported g-code", "Desteklenmeyen G-Code komutu", ErrorSeverity.Error) },
            { 21, ("Modal group required", "Modal grup gerekli", ErrorSeverity.Error) },
            { 22, ("Feed rate required", "Feed rate belirtilmeli", ErrorSeverity.Error) },
            { 23, ("Invalid target", "Geçersiz hedef pozisyon", ErrorSeverity.Error) },
            { 24, ("Arc radius error", "Çember yarıçapı hatası", ErrorSeverity.Error) },
            { 25, ("No offsets", "Offset değeri yok", ErrorSeverity.Warning) },
            { 26, ("Missing attachment", "Eksik bağlantı", ErrorSeverity.Error) },
            { 27, ("Homing not enabled", "Homing aktif değil", ErrorSeverity.Warning) },
            { 28, ("Limits not enabled", "Limit switch'ler aktif değil", ErrorSeverity.Warning) },
            { 29, ("Probe fail", "Probe işlemi başarısız", ErrorSeverity.Error) },
            { 30, ("Invalid probe", "Geçersiz probe komutu", ErrorSeverity.Error) },
            { 31, ("Math error", "Matematik hatası", ErrorSeverity.Error) },
            { 32, ("Max travel exceeded", "Maksimum hareket mesafesi aşıldı", ErrorSeverity.Error) },
            { 33, ("Invalid plane", "Geçersiz çalışma düzlemi", ErrorSeverity.Error) }
        };

        // ✅ FluidNC spesifik hata kodları
        private static readonly Dictionary<int, (string Description, string Solution, ErrorSeverity Severity)> FluidNCErrorCodes =
            new Dictionary<int, (string, string, ErrorSeverity)>
        {
            { 60, ("SD Card error", "SD kart hatası - kartı kontrol edin", ErrorSeverity.Error) },
            { 61, ("Bluetooth error", "Bluetooth bağlantı hatası", ErrorSeverity.Warning) },
            { 62, ("WiFi error", "WiFi bağlantı hatası", ErrorSeverity.Warning) },
            { 63, ("Ethernet error", "Ethernet bağlantı hatası", ErrorSeverity.Warning) },
            { 70, ("Authentication required", "Kimlik doğrulama gerekli", ErrorSeverity.Warning) },
            { 71, ("Authentication failed", "Kimlik doğrulama başarısız", ErrorSeverity.Error) }
        };

        /// <summary>
        /// GRBL Error handling with user-friendly messages
        /// </summary>
        /// <param name="errorCode">GRBL error code</param>
        /// <param name="originalMessage">Original error message</param>
        private void HandleGrblError(int errorCode, string originalMessage)
        {
            try
            {
                bool isFluidNC = originalMessage.Contains("FluidNC");

                // FluidNC error kodlarını önce kontrol et
                if (isFluidNC && FluidNCErrorCodes.ContainsKey(errorCode))
                {
                    var fluidError = FluidNCErrorCodes[errorCode];
                    LogGrblError(errorCode, fluidError.Description, fluidError.Solution, fluidError.Severity, "FluidNC");
                }
                // Standard GRBL error kodları
                else if (GrblErrorCodes.ContainsKey(errorCode))
                {
                    var grblError = GrblErrorCodes[errorCode];
                    LogGrblError(errorCode, grblError.Description, grblError.Solution, grblError.Severity, "GRBL");
                }
                else
                {
                    // Bilinmeyen hata kodu
                    _addLogMessageDelegate?.Invoke($"> ❓ GRBL HATA:{errorCode} - Bilinmeyen hata kodu");
                    _addLogMessageDelegate?.Invoke($"> 📝 Orijinal mesaj: {originalMessage}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandleGrblError error: {ex.Message}");
                _addLogMessageDelegate?.Invoke($"> ❌ HATA İŞLEME HATASI: {originalMessage}");
            }
        }

        /// <summary>
        /// Detailed GRBL error logging
        /// </summary>
        /// <param name="errorCode">Error code</param>
        /// <param name="description">Error description</param>
        /// <param name="solution">Suggested solution</param>
        /// <param name="severity">Error severity</param>
        /// <param name="system">System name (GRBL/FluidNC)</param>
        private void LogGrblError(int errorCode, string description, string solution, ErrorSeverity severity, string system)
        {
            try
            {
                string severityIcon = GetSeverityIcon(severity);

                _addLogMessageDelegate?.Invoke($"> {severityIcon} {system} HATA:{errorCode}");
                _addLogMessageDelegate?.Invoke($">   📋 Açıklama: {description}");
                _addLogMessageDelegate?.Invoke($">   💡 Çözüm: {solution}");

                // Critical error'lar için ek uyarı
                if (severity == ErrorSeverity.Critical)
                {
                    _addLogMessageDelegate?.Invoke($">   🚨 KRİTİK: Makineyi durdurun ve kontrol edin!");
                }

                // Error history'ye de ekle
                lock (_errorLogLock)
                {
                    var errorEntry = $"{system} Error:{errorCode} - {description}";
                    _errorHistory.Enqueue(errorEntry);
                    if (_errorHistory.Count > MAX_ERROR_HISTORY)
                    {
                        _errorHistory.Dequeue();
                    }
                }

                // Update statistics
                UpdateErrorStatistics($"{system}_Error_{errorCode}", severity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogGrblError error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get GRBL error statistics
        /// </summary>
        /// <returns>Dictionary of GRBL error statistics</returns>
        public Dictionary<string, int> GetGrblErrorStatistics()
        {
            var grblStats = new Dictionary<string, int>();

            try
            {
                lock (_errorLogLock)
                {
                    foreach (var error in _errorHistory)
                    {
                        if (error.Contains("GRBL Error:"))
                        {
                            var match = Regex.Match(error, @"Error:(\d+)");
                            if (match.Success)
                            {
                                string errorKey = $"Error:{match.Groups[1].Value}";
                                grblStats[errorKey] = grblStats.ContainsKey(errorKey) ? grblStats[errorKey] + 1 : 1;
                            }
                        }
                        else if (error.Contains("FluidNC Error:"))
                        {
                            var match = Regex.Match(error, @"Error:(\d+)");
                            if (match.Success)
                            {
                                string errorKey = $"FluidNC:{match.Groups[1].Value}";
                                grblStats[errorKey] = grblStats.ContainsKey(errorKey) ? grblStats[errorKey] + 1 : 1;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGrblErrorStatistics error: {ex.Message}");
            }

            return grblStats;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get severity prefix string
        /// </summary>
        /// <param name="severity">Error severity</param>
        /// <returns>Severity prefix</returns>
        private string GetSeverityPrefix(ErrorSeverity severity)
        {
            // ✅ C# 7.3 Compatible - switch expression yerine if-else
            if (severity == ErrorSeverity.Info)
                return "ℹ️ INFO";
            else if (severity == ErrorSeverity.Warning)
                return "⚠️ WARNING";
            else if (severity == ErrorSeverity.Error)
                return "❌ ERROR";
            else if (severity == ErrorSeverity.Critical)
                return "🚨 CRITICAL";
            else
                return "❓ UNKNOWN";
        }

        /// <summary>
        /// Get severity icon
        /// </summary>
        /// <param name="severity">Error severity</param>
        /// <returns>Severity icon</returns>
        private string GetSeverityIcon(ErrorSeverity severity)
        {
            // ✅ C# 7.3 Compatible
            if (severity == ErrorSeverity.Info)
                return "ℹ️";
            else if (severity == ErrorSeverity.Warning)
                return "⚠️";
            else if (severity == ErrorSeverity.Error)
                return "❌";
            else if (severity == ErrorSeverity.Critical)
                return "🚨";
            else
                return "❓";
        }

        /// <summary>
        /// Update error statistics
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <param name="severity">Error severity</param>
        private void UpdateErrorStatistics(string operation, ErrorSeverity severity)
        {
            try
            {
                var key = $"{GetSeverityPrefix(severity)}_{operation}";
                _errorStatistics.AddOrUpdate(key, 1, (k, v) => v + 1);

                // Cleanup old statistics if too many
                if (_errorStatistics.Count > MAX_ERROR_STATISTICS)
                {
                    var oldestKeys = _errorStatistics.Keys.Take(10).ToList();
                    foreach (var oldKey in oldestKeys)
                    {
                        _errorStatistics.TryRemove(oldKey, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateErrorStatistics error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate error operation name
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <returns>Valid operation name</returns>
        private string ValidateOperation(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation))
                return "UnknownOperation";

            // Limit operation name length
            if (operation.Length > 50)
                return operation.Substring(0, 50) + "...";

            return operation;
        }

        /// <summary>
        /// Get error severity distribution statistics
        /// </summary>
        /// <returns>Dictionary with severity counts</returns>
        public Dictionary<ErrorSeverity, int> GetSeverityDistribution()
        {
            var distribution = new Dictionary<ErrorSeverity, int>
            {
                { ErrorSeverity.Info, 0 },
                { ErrorSeverity.Warning, 0 },
                { ErrorSeverity.Error, 0 },
                { ErrorSeverity.Critical, 0 }
            };

            try
            {
                foreach (var severity in new[] { ErrorSeverity.Info, ErrorSeverity.Warning, ErrorSeverity.Error, ErrorSeverity.Critical })
                {
                    distribution[severity] = GetErrorCount(severity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetSeverityDistribution error: {ex.Message}");
            }

            return distribution;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                lock (_errorLogLock)
                {
                    _errorHistory?.Clear();
                }

                _errorStatistics?.Clear();
                _addLogMessageDelegate = null;

                System.Diagnostics.Debug.WriteLine("ErrorHandlingService disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ErrorHandlingService dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}