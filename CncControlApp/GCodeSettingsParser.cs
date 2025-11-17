using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CncControlApp
{
    public class GCodeSettingsParser
    {
        private readonly Dictionary<int, string> _knownSettings;
        private readonly Dictionary<string, string> _fluidncConfigMappings;

        public GCodeSettingsParser()
        {
      _knownSettings = new Dictionary<int, string>
            {
         // Mevcut ayarlar
          { 30, "Spindle maksimum hizi (RPM)" },
          { 110, "X ekseni maksimum hizi (mm/min)" },
          { 111, "Y ekseni maksimum hizi (mm/min)" },
     { 112, "Z ekseni maksimum hizi (mm/min)" },
   
         // A ekseni ayarlari ekle
         { 113, "A ekseni maksimum hizi (mm/min)" },
       
        // Ivme ayarlari ekle  
                { 100, "X ekseni step/mm" },
    { 101, "Y ekseni step/mm" },
      { 102, "Z ekseni step/mm" },
           { 103, "A ekseni step/mm" },
      
             // Ivme ayarlari
        { 120, "X ekseni ivmesi (mm/s2)" },
        { 121, "Y ekseni ivmesi (mm/s2)" },
     { 122, "Z ekseni ivmesi (mm/s2)" },
        { 123, "A ekseni ivmesi (mm/s2)" },
      
       // Diger onemli ayarlar
     { 130, "X ekseni maksimum travel (mm)" },
                { 131, "Y ekseni maksimum travel (mm)" },
     { 132, "Z ekseni maksimum travel (mm)" },
                { 133, "A ekseni maksimum travel (mm)" },
   
           // Home ayarlari
        { 23, "Homing dir invert mask" },
           { 24, "Homing feed rate (mm/min)" },
         { 25, "Homing seek rate (mm/min)" },
     { 27, "Homing pull-off distance (mm)" },
      
    // FluidNC ozel ayarlar
            { 1, "Step idle delay (ms)" },
          { 2, "Step port invert mask" },
       { 3, "Direction port invert mask" },
       { 4, "Step enable invert" },
          { 5, "Limit pins invert" },
       { 6, "Probe pin invert" },
      { 10, "Status report mask" },
          { 11, "Junction deviation (mm)" },
           { 12, "Arc tolerance (mm)" },
        { 13, "Report inches" },
 { 20, "Soft limits" },
            { 21, "Hard limits" },
           { 22, "Homing cycle" },
  { 26, "Homing debounce delay (ms)" },
   { 31, "Spindle minimum RPM" },
      { 32, "Laser mode" }
            };

        // FluidNC Config mappings
            _fluidncConfigMappings = new Dictionary<string, string>
            {
       {"board", "Anakart Bilgisi"},
     {"name", "Makine Adi"},
       {"meta", "Meta Bilgiler"},
     {"stepping", "Step Motor Ayarlari"},
       {"axes", "Eksen Ayarlari"},
           {"motor0", "Motor 0 Ayarlari"},
     {"motor1", "Motor 1 Ayarlari"},
   {"motor2", "Motor 2 Ayarlari"},
  {"steps_per_mm", "Steps/mm"},
   {"max_rate_mm_per_min", "Max Hiz (mm/dak)"},
            {"acceleration_mm_per_sec2", "Ivme (mm/sn2)"},
   {"max_travel_mm", "Max Hareket (mm)"},
        {"soft_limits", "Soft Limitler"},
      {"homing", "Homing Ayarlari"},
     {"cycle", "Homing Cycle"},
                {"positive_direction", "Pozitif Yon"},
    {"mpos_mm", "Homing Pozisyonu"},
        {"feed_mm_per_min", "Homing Hizi"},
          {"seek_mm_per_min", "Homing Arama Hizi"},
            {"debounce_ms", "Debounce (ms)"},
      {"pull_off_mm", "Pull-off (mm)"},
                {"spindle", "Spindle Ayarlari"},
       {"pwm_hz", "PWM Frekansi"},
      {"output_pin", "Cikis Pin"},
        {"enable_pin", "Enable Pin"},
   {"direction_pin", "Yon Pin"},
            {"speed_map", "Hiz Haritasi"},
     {"coolant", "Sogutma Ayarlari"},
     {"mist_pin", "Puskurtme Pin"},
       {"flood_pin", "Sel Pin"},
       {"delay_ms", "Gecikme (ms)"},
    {"probe", "Probe Ayarlari"},
        {"pin", "Pin"},
                {"check_mode_start", "Check Mode"},
          {"control", "Kontrol Ayarlari"},
         {"user_outputs", "Kullanici Cikislari"}
            };
        }

     // GRBL Settings parsing (mevcut)
  public GCodeSetting ParseLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || !rawLine.StartsWith("$"))
       return null;

            var match = Regex.Match(rawLine, @"^\$(\d+)=([\d\.]+)");
            if (!match.Success) return null;

      var setting = new GCodeSetting
            {
    Id = int.Parse(match.Groups[1].Value),
                Value = match.Groups[2].Value
        };

          setting.KnownMeaning = _knownSettings.ContainsKey(setting.Id)
                ? _knownSettings[setting.Id]
          : "Bilinmeyen ayar";

   return setting;
        }

        // FluidNC Config parsing - Iyilestirilmis
        public GCodeSetting ParseFluidNCConfigLine(string rawLine)
        {
   if (string.IsNullOrWhiteSpace(rawLine)) return null;

    try
    {
           string trimmedLine = rawLine.Trim();
    
       // Skip header/footer lines ve grbl responses
  if (trimmedLine.Contains("FluidNC v") || 
             trimmedLine.Contains("---") || 
         trimmedLine.Contains("===") ||
          trimmedLine.StartsWith("Grbl ") ||
           trimmedLine.Contains("'$' for help") ||
       trimmedLine.Equals("ok", StringComparison.OrdinalIgnoreCase))
      return null;

           // Skip comment lines
 if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//"))
      return null;

      GCodeSetting setting = null;

        // Format 1: "key: value" (en yaygin FluidNC format)
        if (trimmedLine.Contains(":") && !trimmedLine.Contains("|"))
 {
   var parts = trimmedLine.Split(new char[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 2)
           {
         string key = parts[0].Trim();
 string value = parts[1].Trim();

// Skip empty values
      if (string.IsNullOrWhiteSpace(value) || value == "null" || value == "none")
                 return null;

            setting = new GCodeSetting
    {
         Id = key.GetHashCode(),
                  Value = value,
         Description = key,
  KnownMeaning = GetFluidNCMeaning(key, key)
               };
      }
           }
      // Format 2: "key = value"
    else if (trimmedLine.Contains(" = "))
      {
          var parts = trimmedLine.Split(new string[] { " = " }, 2, StringSplitOptions.RemoveEmptyEntries);
     if (parts.Length == 2)
     {
              string key = parts[0].Trim();
   string value = parts[1].Trim();

          if (!string.IsNullOrWhiteSpace(value) && value != "null")
        {
         setting = new GCodeSetting
                 {
         Id = key.GetHashCode(),
     Value = value,
               Description = key,
              KnownMeaning = GetFluidNCMeaning(key, key)
         };
            }
        }
   }
    // Format 3: Section headers like "[axes]"
                else if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
           {
         string sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2);
  setting = new GCodeSetting
    {
       Id = sectionName.GetHashCode(),
   Value = "section",
            Description = $"[{sectionName}]",
KnownMeaning = $"FluidNC configuration section: {sectionName}"
           };
          }

           return setting;
     }
      catch (Exception ex)
            {
     System.Diagnostics.Debug.WriteLine($"FluidNC parse error: {ex.Message} for line: {rawLine}");
     return null;
}
        }

        // Key-value pair parsing
        private GCodeSetting ParseKeyValuePair(string originalLine, string trimmedLine)
     {
var parts = trimmedLine.Split(new char[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
   if (parts.Length != 2) return null;

   string key = parts[0].Trim();
        string value = parts[1].Trim();

            // Skip empty values
 if (string.IsNullOrWhiteSpace(value) || value == "null" || value == "none")
          return null;

      // Calculate indentation level for nested structure
            int indentLevel = originalLine.Length - originalLine.TrimStart().Length;
  string fullPath = BuildFullPath(originalLine, key, indentLevel);

    return new GCodeSetting
      {
           Id = fullPath.GetHashCode(),
           Value = value,
             Description = fullPath,
          KnownMeaning = GetFluidNCMeaning(fullPath, key)
        };
        }

// Assignment pair parsing (key = value)
  private GCodeSetting ParseAssignmentPair(string originalLine, string trimmedLine)
        {
       var parts = trimmedLine.Split(new char[] { '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;

        string key = parts[0].Trim();
            string value = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(value))
     return null;

       int indentLevel = originalLine.Length - originalLine.TrimStart().Length;
     string fullPath = BuildFullPath(originalLine, key, indentLevel);

            return new GCodeSetting
      {
                Id = fullPath.GetHashCode(),
      Value = value,
         Description = fullPath,
           KnownMeaning = GetFluidNCMeaning(fullPath, key)
            };
        }

        // Section header parsing
     private GCodeSetting ParseSectionHeader(string originalLine, string trimmedLine)
        {
   string sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2); // Remove [ ]
    
            return new GCodeSetting
            {
           Id = sectionName.GetHashCode(),
          Value = "SECTION",
    Description = sectionName,
          KnownMeaning = GetFluidNCMeaning(sectionName, sectionName) + " (Section)"
            };
        }

 // Build full path for nested parameters
  private string BuildFullPath(string originalLine, string key, int indentLevel)
      {
            // For now, return the key as-is
  // In a full implementation, you'd track the current section/parent context
            return key;
     }

        // FluidNC parameter meaning lookup - Iyilestirilmis
        private string GetFluidNCMeaning(string fullPath, string key)
        {
            // Exact match
       if (_fluidncConfigMappings.TryGetValue(key, out string exactMeaning))
   {
 return exactMeaning;
          }

            // Partial matches for common patterns
       if (key.Contains("max_rate") || key.Contains("max_speed"))
return "Maksimum Hiz";
   if (key.Contains("acceleration"))
         return "Ivme";
     if (key.Contains("steps_per_mm"))
      return "Steps/mm";
    if (key.Contains("max_travel"))
        return "Maksimum Hareket";
        if (key.Contains("homing"))
        return "Homing Ayari";
         if (key.Contains("pin"))
return "Pin Ayari";
     if (key.Contains("enable"))
         return "Enable Ayari";
      if (key.Contains("direction"))
     return "Yon Ayari";

            // Try partial matches from mapping
            var partialMatch = _fluidncConfigMappings.Keys
     .FirstOrDefault(mapKey => key.Contains(mapKey) || mapKey.Contains(key));

    if (!string.IsNullOrEmpty(partialMatch))
    {
           return _fluidncConfigMappings[partialMatch];
    }

 return "FluidNC Config Parameter";
        }

        // Check if line is FluidNC config - Iyilestirilmis
        public bool IsFluidNCConfigLine(string line)
 {
            if (string.IsNullOrWhiteSpace(line)) return false;

         try
          {
    string trimmed = line.Trim();
       
 // Skip GRBL responses ve "ok" mesajlari
        if (trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
      trimmed.StartsWith("<") && trimmed.EndsWith(">") ||
         trimmed.StartsWith("$") && char.IsDigit(trimmed.ElementAtOrDefault(1)) ||
    trimmed.StartsWith("Grbl ") ||
          trimmed.Contains("FluidNC v") ||
  trimmed.Contains("'$' for help"))
          {
 return false;
     }
    
          // FluidNC config patterns - ENHANCED
        return trimmed.Contains(":") ||     // key: value format
       trimmed.Contains(" = ") ||              // key = value format  
      (trimmed.StartsWith("[") && trimmed.EndsWith("]")) ||  // [section] format
     trimmed.Contains("spindle") ||       // spindle related
      trimmed.Contains("axes") ||    // axes config
  trimmed.Contains("motor") ||         // motor config
            trimmed.Contains("stepper") ||              // stepper config
   trimmed.Contains("limit_") ||   // limit switches
 trimmed.Contains("_pin") ||      // pin configs
  (trimmed.Contains("reset_") && !trimmed.StartsWith("Grbl")) ||  // reset configs
      (trimmed.Contains("must_home") || trimmed.Contains("verbose_errors")); // boolean configs
            }
       catch (Exception)
            {
            return false;
       }
        }

        // Extract numeric value from FluidNC config
public bool TryExtractNumericValue(string configValue, out double numericValue)
   {
            numericValue = 0;
         
            if (string.IsNullOrWhiteSpace(configValue))
       return false;

         // Handle different formats
         string cleanValue = configValue.Trim().ToLower();
   
     // Remove units if present
     cleanValue = cleanValue.Replace("mm/min", "").Replace("mm", "").Replace("rpm", "").Trim();
            
// Handle boolean values
  if (cleanValue == "true" || cleanValue == "on" || cleanValue == "yes")
       {
           numericValue = 1;
  return true;
   }
            if (cleanValue == "false" || cleanValue == "off" || cleanValue == "no")
         {
     numericValue = 0;
        return true;
            }

   // Parse numeric value
      return double.TryParse(cleanValue, NumberStyles.Any, CultureInfo.InvariantCulture, out numericValue);
        }
    }
}
