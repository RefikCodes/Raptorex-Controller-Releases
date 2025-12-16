using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CncControlApp.Services
{
    /// <summary>
    /// GCode dosyasından resume için gerekli modal state'leri çıkarır
    /// </summary>
    public class GCodeResumeService
    {
        #region Modal State Class
        
        /// <summary>
        /// Bir satıra kadar olan tüm modal state'leri tutar
        /// </summary>
        public class GCodeModalState
        {
            // Koordinat sistemi
            public string CoordinateSystem { get; set; } = "G54"; // G54-G59
            
            // Distance mode
            public string DistanceMode { get; set; } = "G90"; // G90 Absolute, G91 Incremental
            
            // Units
            public string Units { get; set; } = "G21"; // G20 inch, G21 mm
            
            // Feed rate mode
            public string FeedRateMode { get; set; } = "G94"; // G93 inverse time, G94 units/min
            
            // Motion mode
            public string MotionMode { get; set; } = "G0"; // G0, G1, G2, G3
            
            // Plane selection
            public string Plane { get; set; } = "G17"; // G17 XY, G18 ZX, G19 YZ
            
            // Spindle
            public double SpindleSpeed { get; set; } = 0; // S value
            public string SpindleState { get; set; } = "M5"; // M3 CW, M4 CCW, M5 Stop
            
            // Feed rate
            public double FeedRate { get; set; } = 0; // F value
            
            // Coolant
            public string CoolantState { get; set; } = "M9"; // M7 mist, M8 flood, M9 off
            
            // Tool
            public int ToolNumber { get; set; } = 0; // T value
            
            // Son pozisyon (hesaplanabilir)
            public double LastX { get; set; } = 0;
            public double LastY { get; set; } = 0;
            public double LastZ { get; set; } = 0;
            
            // Hangi satırdan alındı
            public int FromLine { get; set; } = 0;
            
            // Güvenli Z yüksekliği (resume sırasında XY hareketinden önce Z bu yüksekliğe çekilir)
            public const double SafeZHeight = 30.0;
            
            /// <summary>
            /// Resume için gerekli GCode komutlarını oluşturur.
            /// Sıralama: Z güvenli yüksekliğe -> XY konumuna -> Z çalışma konumuna
            /// </summary>
            public List<string> GenerateResumeCommands()
            {
                var commands = new List<string>();
                
                // 1. Units ve distance mode
                commands.Add($"{Units} {DistanceMode}");
                
                // 2. Plane selection
                commands.Add(Plane);
                
                // 3. Koordinat sistemi
                commands.Add(CoordinateSystem);
                
                // 4. Feed rate mode
                commands.Add(FeedRateMode);
                
                // 5. Spindle (eğer açıksa)
                if (SpindleState == "M3" || SpindleState == "M4")
                {
                    commands.Add($"{SpindleState} S{SpindleSpeed}");
                }
                
                // 6. Feed rate set
                if (FeedRate > 0)
                {
                    commands.Add($"F{FeedRate}");
                }
                
                // 7. Coolant (eğer açıksa)
                if (CoolantState == "M7" || CoolantState == "M8")
                {
                    commands.Add(CoolantState);
                }
                
                // 8. GÜVENLİ POZİSYONA GİT:
                // 8a. Önce Z'yi güvenli yüksekliğe çek (hızlı hareket)
                commands.Add($"G0 Z{SafeZHeight.ToString("F3", CultureInfo.InvariantCulture)}");
                
                // 8b. XY konumuna git (hızlı hareket)
                commands.Add($"G0 X{LastX.ToString("F3", CultureInfo.InvariantCulture)} Y{LastY.ToString("F3", CultureInfo.InvariantCulture)}");
                
                // 8c. Z'yi çalışma konumuna indir (hızlı hareket)
                commands.Add($"G0 Z{LastZ.ToString("F3", CultureInfo.InvariantCulture)}");
                
                return commands;
            }
            
            /// <summary>
            /// State'i okunabilir string olarak döndürür
            /// </summary>
            public override string ToString()
            {
                return $"📍 Modal State (Satır {FromLine})\n" +
                       $"━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                       $"🎯 Koordinat: {CoordinateSystem}\n" +
                       $"📐 Mod: {DistanceMode} ({(DistanceMode == "G90" ? "Absolute" : "Incremental")})\n" +
                       $"📏 Birim: {Units} ({(Units == "G21" ? "mm" : "inch")})\n" +
                       $"✈️ Hareket: {MotionMode}\n" +
                       $"📊 Düzlem: {Plane}\n" +
                       $"🔄 Spindle: {SpindleState} @ S{SpindleSpeed}\n" +
                       $"⚡ Feed: F{FeedRate}\n" +
                       $"💧 Coolant: {CoolantState}\n" +
                       $"🔧 Tool: T{ToolNumber}";
            }
        }
        
        #endregion
        
        #region Preamble Detection
        
        /// <summary>
        /// Dosyanın başındaki hazırlık satırlarını bulur (genellikle ilk hareket komutuna kadar)
        /// </summary>
        public int FindPreambleEndLine(List<string> gCodeLines)
        {
            for (int i = 0; i < gCodeLines.Count; i++)
            {
                string line = gCodeLines[i].ToUpper().Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("("))
                    continue;
                
                // İlk hareket komutu (X, Y, Z koordinatı içeren)
                if (HasMovementCoordinate(line) && 
                    (line.Contains("G0") || line.Contains("G1") || line.Contains("G2") || line.Contains("G3")))
                {
                    return i; // Bu satırdan öncesi preamble
                }
            }
            
            return 0;
        }
        
        /// <summary>
        /// Preamble satırlarını döndürür
        /// </summary>
        public List<string> GetPreambleLines(List<string> gCodeLines)
        {
            int endLine = FindPreambleEndLine(gCodeLines);
            var preamble = new List<string>();
            
            for (int i = 0; i < endLine; i++)
            {
                string line = gCodeLines[i].Trim();
                if (!string.IsNullOrEmpty(line) && !line.StartsWith(";") && !line.StartsWith("("))
                {
                    preamble.Add(line);
                }
            }
            
            return preamble;
        }
        
        #endregion
        
        #region Modal State Extraction
        
        /// <summary>
        /// Belirtilen satıra kadar olan modal state'leri çıkarır
        /// </summary>
        public GCodeModalState ExtractModalStateUpToLine(List<string> gCodeLines, int targetLine)
        {
            var state = new GCodeModalState();
            state.FromLine = targetLine;
            
            // 0'dan targetLine'a kadar tüm satırları tara
            for (int i = 0; i <= targetLine && i < gCodeLines.Count; i++)
            {
                ParseLineForModalState(gCodeLines[i], state);
            }
            
            return state;
        }
        
        /// <summary>
        /// Tek bir satırı parse ederek modal state'i günceller
        /// </summary>
        private void ParseLineForModalState(string line, GCodeModalState state)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            
            string upper = RemoveComments(line).ToUpper();
            if (string.IsNullOrWhiteSpace(upper)) return;
            
            // Coordinate system (G54-G59)
            if (Regex.IsMatch(upper, @"\bG5[4-9]\b"))
            {
                var match = Regex.Match(upper, @"\bG5([4-9])\b");
                if (match.Success) state.CoordinateSystem = "G5" + match.Groups[1].Value;
            }
            
            // Distance mode
            if (upper.Contains("G90")) state.DistanceMode = "G90";
            else if (upper.Contains("G91")) state.DistanceMode = "G91";
            
            // Units
            if (upper.Contains("G20")) state.Units = "G20";
            else if (upper.Contains("G21")) state.Units = "G21";
            
            // Feed rate mode
            if (upper.Contains("G93")) state.FeedRateMode = "G93";
            else if (upper.Contains("G94")) state.FeedRateMode = "G94";
            
            // Motion mode
            if (Regex.IsMatch(upper, @"\bG0\b|\bG00\b")) state.MotionMode = "G0";
            else if (Regex.IsMatch(upper, @"\bG1\b|\bG01\b")) state.MotionMode = "G1";
            else if (Regex.IsMatch(upper, @"\bG2\b|\bG02\b")) state.MotionMode = "G2";
            else if (Regex.IsMatch(upper, @"\bG3\b|\bG03\b")) state.MotionMode = "G3";
            
            // Plane selection
            if (upper.Contains("G17")) state.Plane = "G17";
            else if (upper.Contains("G18")) state.Plane = "G18";
            else if (upper.Contains("G19")) state.Plane = "G19";
            
            // Spindle
            var sMatch = Regex.Match(upper, @"S(\d+\.?\d*)");
            if (sMatch.Success)
            {
                double.TryParse(sMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double s);
                state.SpindleSpeed = s;
            }
            if (upper.Contains("M3")) state.SpindleState = "M3";
            else if (upper.Contains("M4")) state.SpindleState = "M4";
            else if (upper.Contains("M5")) state.SpindleState = "M5";
            
            // Feed rate
            var fMatch = Regex.Match(upper, @"F(\d+\.?\d*)");
            if (fMatch.Success)
            {
                double.TryParse(fMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double f);
                state.FeedRate = f;
            }
            
            // Coolant
            if (upper.Contains("M7")) state.CoolantState = "M7";
            else if (upper.Contains("M8")) state.CoolantState = "M8";
            else if (upper.Contains("M9")) state.CoolantState = "M9";
            
            // Tool
            var tMatch = Regex.Match(upper, @"T(\d+)");
            if (tMatch.Success)
            {
                int.TryParse(tMatch.Groups[1].Value, out int t);
                state.ToolNumber = t;
            }
            
            // Pozisyon (basit parsing - absolute mode varsayımıyla)
            var xMatch = Regex.Match(upper, @"X(-?\d+\.?\d*)");
            if (xMatch.Success)
            {
                double.TryParse(xMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
                state.LastX = x;
            }
            
            var yMatch = Regex.Match(upper, @"Y(-?\d+\.?\d*)");
            if (yMatch.Success)
            {
                double.TryParse(yMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
                state.LastY = y;
            }
            
            var zMatch = Regex.Match(upper, @"Z(-?\d+\.?\d*)");
            if (zMatch.Success)
            {
                double.TryParse(zMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double z);
                state.LastZ = z;
            }
        }
        
        #endregion
        
        #region Helpers
        
        private string RemoveComments(string line)
        {
            // Semicolon comments
            int idx = line.IndexOf(';');
            if (idx >= 0) line = line.Substring(0, idx);
            
            // Parentheses comments
            int open = line.IndexOf('(');
            if (open >= 0)
            {
                int close = line.IndexOf(')', open);
                if (close >= 0) line = line.Remove(open, close - open + 1);
            }
            
            return line.Trim();
        }
        
        private bool HasMovementCoordinate(string line)
        {
            return Regex.IsMatch(line, @"[XYZ]-?\d");
        }
        
        #endregion
    }
}
