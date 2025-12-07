using System;

namespace GrblStreamer.Models
{
    /// <summary>
    /// Streaming istatistikleri
    /// </summary>
    public class StreamingStats
    {
        /// <summary>Toplam satır sayısı</summary>
        public int TotalLines { get; set; }
        
        /// <summary>Gönderilen satır sayısı</summary>
        public int SentLines { get; set; }
        
        /// <summary>Tamamlanan satır sayısı (OK alınan)</summary>
        public int CompletedLines { get; set; }
        
        /// <summary>Kuyrukta bekleyen satır sayısı</summary>
        public int QueuedLines
        {
            get { return TotalLines - SentLines; }
        }
        
        /// <summary>Buffer'da bekleyen satır sayısı</summary>
        public int BufferedLines
        {
            get { return SentLines - CompletedLines; }
        }
        
        /// <summary>İlerleme yüzdesi</summary>
        public double ProgressPercent
        {
            get { return TotalLines > 0 ? (CompletedLines * 100.0 / TotalLines) : 0; }
        }
        
        /// <summary>İş başlangıç zamanı</summary>
        public DateTime? StartTime { get; set; }
        
        /// <summary>İş bitiş zamanı</summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>Geçen süre</summary>
        public TimeSpan ElapsedTime
        {
            get { return (EndTime ?? DateTime.Now) - (StartTime ?? DateTime.Now); }
        }
        
        /// <summary>Tahmini kalan süre</summary>
        public TimeSpan EstimatedRemaining
        {
            get
            {
                if (CompletedLines == 0 || TotalLines == 0) return TimeSpan.Zero;
                var elapsed = ElapsedTime.TotalSeconds;
                var remaining = (elapsed / CompletedLines) * (TotalLines - CompletedLines);
                return TimeSpan.FromSeconds(remaining);
            }
        }
    }
}
