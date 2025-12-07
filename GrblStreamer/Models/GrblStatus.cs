using System.Collections.Generic;
using GrblStreamer.Enums;

namespace GrblStreamer.Models
{
    /// <summary>
    /// GRBL durum bilgileri - ? sorgusuna gelen yanıt
    /// </summary>
    public class GrblStatus
    {
        /// <summary>Makine durumu (Idle, Run, Hold, vb.)</summary>
        public GrblState State { get; set; } = GrblState.Unknown;
        
        /// <summary>Machine Position (MPos)</summary>
        public MachinePosition MachinePosition { get; set; } = new MachinePosition();
        
        /// <summary>Work Position (WPos)</summary>
        public MachinePosition WorkPosition { get; set; } = new MachinePosition();
        
        /// <summary>Work Coordinate Offset</summary>
        public WorkOffset WorkOffset { get; set; } = new WorkOffset();
        
        /// <summary>Planner buffer kullanılabilir blok sayısı</summary>
        public int PlannerBuffer { get; set; }
        
        /// <summary>RX buffer kullanılabilir byte sayısı</summary>
        public int RxBuffer { get; set; }
        
        /// <summary>Feed Override yüzdesi</summary>
        public int FeedOverride { get; set; } = 100;
        
        /// <summary>Spindle Override yüzdesi</summary>
        public int SpindleOverride { get; set; } = 100;
        
        /// <summary>Rapid Override yüzdesi</summary>
        public int RapidOverride { get; set; } = 100;
        
        /// <summary>Aktif giriş pinleri (X, Y, Z, P, D, H, R, S)</summary>
        public List<char> InputPins { get; set; } = new List<char>();
        
        /// <summary>Spindle hızı (RPM)</summary>
        public int SpindleSpeed { get; set; }
        
        /// <summary>Feed rate (mm/dk)</summary>
        public double FeedRate { get; set; }
        
        /// <summary>Ham durum stringi</summary>
        public string RawStatus { get; set; } = string.Empty;
    }
}
