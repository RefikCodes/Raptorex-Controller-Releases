using GrblStreamer.Enums;

namespace GrblStreamer.Models
{
    /// <summary>
    /// Firmware bilgileri
    /// </summary>
    public class FirmwareInfo
    {
        /// <summary>Firmware türü (grbl, grblHAL, FluidNC)</summary>
        public FirmwareType Type { get; set; } = FirmwareType.Unknown;
        
        /// <summary>Firmware platform adı</summary>
        public string Platform { get; set; } = string.Empty;
        
        /// <summary>Firmware versiyonu</summary>
        public string Version { get; set; } = string.Empty;
        
        /// <summary>Build tarihi</summary>
        public string BuildDate { get; set; } = string.Empty;
        
        /// <summary>RX Buffer boyutu</summary>
        public int RxBufferSize { get; set; } = 127;
        
        /// <summary>Opsiyonlar (VL, A, etc.)</summary>
        public string Options { get; set; } = string.Empty;

        public override string ToString()
        {
            return string.Format("{0} v{1}", Platform, Version);
        }
    }
}
