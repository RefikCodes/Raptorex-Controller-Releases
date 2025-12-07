namespace GrblStreamer.Core
{
    /// <summary>
    /// GRBL realtime komutları (buffer'a girmez, anında işlenir)
    /// </summary>
    public static class GrblRealtime
    {
        /// <summary>Soft Reset (Ctrl+X)</summary>
        public const byte SoftReset = 0x18;
        
        /// <summary>Status Query</summary>
        public const char StatusQuery = '?';
        
        /// <summary>Cycle Start / Resume</summary>
        public const char CycleStart = '~';
        
        /// <summary>Feed Hold / Pause</summary>
        public const char FeedHold = '!';
        
        /// <summary>Jog Cancel</summary>
        public const byte JogCancel = 0x85;
        
        /// <summary>Feed Override: 100%</summary>
        public const byte FeedOverride100 = 0x90;
        
        /// <summary>Feed Override: +10%</summary>
        public const byte FeedOverrideInc10 = 0x91;
        
        /// <summary>Feed Override: -10%</summary>
        public const byte FeedOverrideDec10 = 0x92;
        
        /// <summary>Feed Override: +1%</summary>
        public const byte FeedOverrideInc1 = 0x93;
        
        /// <summary>Feed Override: -1%</summary>
        public const byte FeedOverrideDec1 = 0x94;
        
        /// <summary>Rapid Override: 100%</summary>
        public const byte RapidOverride100 = 0x95;
        
        /// <summary>Rapid Override: 50%</summary>
        public const byte RapidOverride50 = 0x96;
        
        /// <summary>Rapid Override: 25%</summary>
        public const byte RapidOverride25 = 0x97;
        
        /// <summary>Spindle Override: 100%</summary>
        public const byte SpindleOverride100 = 0x99;
        
        /// <summary>Spindle Override: +10%</summary>
        public const byte SpindleOverrideInc10 = 0x9A;
        
        /// <summary>Spindle Override: -10%</summary>
        public const byte SpindleOverrideDec10 = 0x9B;
        
        /// <summary>Spindle Override: +1%</summary>
        public const byte SpindleOverrideInc1 = 0x9C;
        
        /// <summary>Spindle Override: -1%</summary>
        public const byte SpindleOverrideDec1 = 0x9D;
        
        /// <summary>Toggle Spindle Stop</summary>
        public const byte SpindleStop = 0x9E;
        
        /// <summary>Toggle Flood Coolant</summary>
        public const byte CoolantFloodToggle = 0xA0;
        
        /// <summary>Toggle Mist Coolant</summary>
        public const byte CoolantMistToggle = 0xA1;
        
        /// <summary>Safety Door</summary>
        public const byte SafetyDoor = 0x84;
    }
}
