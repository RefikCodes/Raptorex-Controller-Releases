namespace GrblStreamer.Enums
{
    /// <summary>
    /// GRBL bağlantı durumları
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>Bağlı değil</summary>
        Disconnected = 0,
        
        /// <summary>Bağlı, boşta</summary>
        Connected = 1,
        
        /// <summary>Bağlı, hazır</summary>
        Ready = 2,
        
        /// <summary>GCODE çalıştırılıyor</summary>
        Streaming = 3,
        
        /// <summary>Duraklatılmış</summary>
        Paused = 4,
        
        /// <summary>Alarm durumu</summary>
        Alarm = 5,
        
        /// <summary>Firmware güncelleme modu</summary>
        FirmwareUpgrade = 6
    }
}
