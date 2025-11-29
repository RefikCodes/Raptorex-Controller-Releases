using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace CncControlApp
{
    /// <summary>
    /// Merkezi hata loglama sistemi.
    /// Tüm hatalar masaüstüne anlaşılır şekilde loglanır.
    /// </summary>
    public static class ErrorLogger
    {
        private static readonly object _lockObj = new object();
        private static string _logFilePath;
        private static bool _initialized = false;
        
        /// <summary>
        /// Debug modu - true olduğunda tüm detaylı loglar yazılır
        /// </summary>
        public static bool DebugMode { get; set; } = true;

        /// <summary>
        /// Log dosyasının tam yolu (Masaüstü/RaptorexController_ErrorLog.txt)
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    _logFilePath = Path.Combine(desktop, "RaptorexController_ErrorLog.txt");
                }
                return _logFilePath;
            }
        }

        /// <summary>
        /// Logger'ı başlatır ve uygulama başlangıç bilgisini yazar.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine($"  RAPTOREX CONTROLLER - Uygulama Başlatıldı");
                sb.AppendLine($"  Tarih/Saat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Versiyon: {GetAppVersion()}");
                sb.AppendLine($"  İşletim Sistemi: {Environment.OSVersion}");
                sb.AppendLine($"  .NET Versiyon: {Environment.Version}");
                sb.AppendLine($"  Bilgisayar Adı: {Environment.MachineName}");
                sb.AppendLine($"  Kullanıcı: {Environment.UserName}");
                sb.AppendLine("════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine();

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Log dosyası oluşturulamadıysa sessizce devam et
            }
        }

        /// <summary>
        /// Hata mesajı loglar.
        /// </summary>
        /// <param name="message">Hata mesajı</param>
        /// <param name="exception">Exception nesnesi (opsiyonel)</param>
        /// <param name="callerFilePath">Çağıran dosya yolu (otomatik)</param>
        /// <param name="callerLineNumber">Çağıran satır numarası (otomatik)</param>
        /// <param name="callerMemberName">Çağıran metod adı (otomatik)</param>
        public static void LogError(
            string message,
            Exception exception = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerMemberName] string callerMemberName = "")
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("────────────────────────────────────────────────────────────────────────────────");
                sb.AppendLine($"🔴 HATA - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"────────────────────────────────────────────────────────────────────────────────");
                sb.AppendLine($"  📍 Konum: {Path.GetFileName(callerFilePath)} → {callerMemberName}() [Satır: {callerLineNumber}]");
                sb.AppendLine($"  📝 Mesaj: {message}");

                if (exception != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  ⚠️ Exception Tipi: {exception.GetType().FullName}");
                    sb.AppendLine($"  ⚠️ Exception Mesajı: {exception.Message}");
                    
                    if (exception.InnerException != null)
                    {
                        sb.AppendLine($"  ⚠️ Inner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
                    }

                    sb.AppendLine();
                    sb.AppendLine("  📋 Stack Trace:");
                    var stackLines = exception.StackTrace?.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    if (stackLines != null)
                    {
                        foreach (var line in stackLines)
                        {
                            sb.AppendLine($"     {line.Trim()}");
                        }
                    }
                }

                sb.AppendLine();

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }

                // Debug modda konsola da yaz
                System.Diagnostics.Debug.WriteLine($"[ERROR] {Path.GetFileName(callerFilePath)}:{callerLineNumber} - {message}");
                if (exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXCEPTION] {exception.GetType().Name}: {exception.Message}");
                }
            }
            catch
            {
                // Loglama başarısız olursa sessizce devam et
            }
        }

        /// <summary>
        /// Uyarı mesajı loglar.
        /// </summary>
        public static void LogWarning(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerMemberName] string callerMemberName = "")
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"🟡 UYARI [{DateTime.Now:HH:mm:ss.fff}] {Path.GetFileName(callerFilePath)} → {callerMemberName}() [Satır: {callerLineNumber}]");
                sb.AppendLine($"   {message}");
                sb.AppendLine();

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }

                System.Diagnostics.Debug.WriteLine($"[WARNING] {Path.GetFileName(callerFilePath)}:{callerLineNumber} - {message}");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Bilgi mesajı loglar.
        /// </summary>
        public static void LogInfo(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerMemberName] string callerMemberName = "")
        {
            try
            {
                var line = $"🟢 BİLGİ [{DateTime.Now:HH:mm:ss.fff}] {Path.GetFileName(callerFilePath)} → {callerMemberName}(): {message}";

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }

                System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Debug mesajı loglar (sadece DebugMode=true ise).
        /// </summary>
        public static void LogDebug(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerMemberName] string callerMemberName = "")
        {
            if (!DebugMode) return;
            
            try
            {
                var line = $"🔵 DEBUG [{DateTime.Now:HH:mm:ss.fff}] {Path.GetFileName(callerFilePath)}:{callerLineNumber} → {callerMemberName}(): {message}";

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }

                System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Kritik hata loglar ve kullanıcıya bildirir.
        /// </summary>
        public static void LogCritical(
            string message,
            Exception exception = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerMemberName] string callerMemberName = "")
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine("║                           ⛔ KRİTİK HATA ⛔                                  ║");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine($"  ⏰ Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"  📍 Konum: {Path.GetFileName(callerFilePath)} → {callerMemberName}() [Satır: {callerLineNumber}]");
                sb.AppendLine($"  📝 Mesaj: {message}");

                if (exception != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  ⚠️ Exception: {exception.GetType().FullName}");
                    sb.AppendLine($"  ⚠️ Detay: {exception.Message}");
                    
                    if (exception.InnerException != null)
                    {
                        sb.AppendLine($"  ⚠️ Inner: {exception.InnerException.Message}");
                    }

                    sb.AppendLine();
                    sb.AppendLine("  📋 Stack Trace:");
                    var stackLines = exception.StackTrace?.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    if (stackLines != null)
                    {
                        foreach (var line in stackLines)
                        {
                            sb.AppendLine($"     {line.Trim()}");
                        }
                    }
                }

                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine();

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Uygulama kapatılırken son bilgiyi yazar.
        /// </summary>
        public static void LogShutdown()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine($"  RAPTOREX CONTROLLER - Uygulama Kapatıldı");
                sb.AppendLine($"  Tarih/Saat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine();

                lock (_lockObj)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "Bilinmiyor";
            }
        }
    }
}
