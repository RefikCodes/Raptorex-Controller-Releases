namespace CncControlApp
{
    /// <summary>
    /// Simple 3D point structure for G-code visualization
    /// </summary>
    public struct Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// G-code movement types
    /// </summary>
    public enum GCodeMovementType
    {
        Rapid,       // G00 - Rapid positioning
        Linear,      // G01 - Linear interpolation
        Arc,         // G02/G03 - Circular interpolation
        Unknown
    }

    /// <summary>
    /// G-code path segment for visualization
    /// </summary>
    public class GCodeSegment
    {
        public Point3D StartPoint { get; set; }
        public Point3D EndPoint { get; set; }
        public GCodeMovementType MovementType { get; set; }
        public double FeedRate { get; set; }
        public int LineNumber { get; set; }
        public string OriginalCode { get; set; }

        // 🆕 Arc-specific properties
        public double ArcCenterI { get; set; } = 0;    // I parameter (X center offset)
        public double ArcCenterJ { get; set; } = 0;    // J parameter (Y center offset)  
        public double ArcCenterK { get; set; } = 0;    // K parameter (Z center offset)
        public double? ArcRadius { get; set; }         // R parameter (radius)
        public bool IsClockwise { get; set; } = false; // G02 = true, G03 = false

        /// <summary>
        /// 🆕 Get the actual distance of this segment (including proper arc calculation)
        /// </summary>
        public double GetActualDistance()
        {
            // This will be calculated by GCodeParser.CalculateSegmentDistance
            // We store it here for performance
            return _cachedDistance ?? 0;
        }

        private double? _cachedDistance;

        /// <summary>
        /// 🆕 Set the calculated distance (called by parser)
        /// </summary>
        public void SetCalculatedDistance(double distance)
        {
            _cachedDistance = distance;
        }
    }

    /// <summary>
    /// Viewport types for multi-panel layout
    /// </summary>
    public enum ViewportType
    {
        Top,        // X-Y view (looking down from +Z)
        Front,      // X-Z view (looking from +Y)
        Right,      // Y-Z view (looking from +X)
        Isometric   // 3D isometric view
    }
}