using System;

namespace CncControlApp.Models
{
    /// <summary>
    /// Represents a single probe measurement record
    /// </summary>
    public class ProbeRecord
    {
        public int Id { get; set; }
        public string Type { get; set; }  // +X, -X, +Y, -Y, -Z, Center X, etc.
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public DateTime Timestamp { get; set; }

        public ProbeRecord()
        {
            Timestamp = DateTime.Now;
        }

        public ProbeRecord(string type, double x, double y, double z) : this()
        {
            Type = type;
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"{Type},{X:F3},{Y:F3},{Z:F3},{Timestamp:yyyy-MM-dd HH:mm:ss}";
        }

        public static ProbeRecord FromCsvLine(string line)
        {
            var parts = line.Split(',');
            if (parts.Length >= 5)
            {
                return new ProbeRecord
                {
                    Type = parts[0],
                    X = double.TryParse(parts[1], out var x) ? x : 0,
                    Y = double.TryParse(parts[2], out var y) ? y : 0,
                    Z = double.TryParse(parts[3], out var z) ? z : 0,
                    Timestamp = DateTime.TryParse(parts[4], out var ts) ? ts : DateTime.Now
                };
            }
            return null;
        }
    }
}
