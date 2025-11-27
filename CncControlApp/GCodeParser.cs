using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CncControlApp
{
    /// <summary>
    /// Handles G-code parsing and interpretation
    /// </summary>
    public class GCodeParser
    {
        #region Fields

        private Point3D _currentPosition = new Point3D(0, 0, 0);
        private bool _absoluteMode = true; // G90/G91
        private double _currentFeedRate = 0; // 🆕 Modal feed rate state

        #endregion

        #region Public Methods

        /// <summary>
        /// Parse a single G-code line for visualization
        /// </summary>
        /// <param name="line">G-code line</param>
        /// <param name="lineNumber">Line number</param>
        /// <param name="segments">List to add parsed segments to</param>
        public void ParseGCodeLine(string line, int lineNumber, List<GCodeSegment> segments)
        {
            try
            {
                // Remove comments and convert to uppercase
                string cleanLine = RemoveComments(line).ToUpper().Trim();
                if (string.IsNullOrEmpty(cleanLine)) return;

                // Parse coordinates
                var x = ExtractCoordinate(cleanLine, 'X');
                var y = ExtractCoordinate(cleanLine, 'Y');
                var z = ExtractCoordinate(cleanLine, 'Z');
                var f = ExtractCoordinate(cleanLine, 'F');

                // 🆕 Parse arc parameters
                var i = ExtractCoordinate(cleanLine, 'I');
                var j = ExtractCoordinate(cleanLine, 'J');
                var k = ExtractCoordinate(cleanLine, 'K');
                var r = ExtractCoordinate(cleanLine, 'R');

                // 🆕 Update modal feed rate if F is specified
                if (f.HasValue)
                {
                    _currentFeedRate = f.Value;
                }

                // Determine movement type
                GCodeMovementType movementType = GCodeMovementType.Unknown;
                bool isClockwise = false;
                
                if (cleanLine.Contains("G00") || cleanLine.Contains("G0 "))
                    movementType = GCodeMovementType.Rapid;
                else if (cleanLine.Contains("G01") || cleanLine.Contains("G1 "))
                    movementType = GCodeMovementType.Linear;
                else if (cleanLine.Contains("G02") || cleanLine.Contains("G2 "))
                {
                    movementType = GCodeMovementType.Arc;
                    isClockwise = true;
                }
                else if (cleanLine.Contains("G03") || cleanLine.Contains("G3 "))
                {
                    movementType = GCodeMovementType.Arc;
                    isClockwise = false;
                }

                // Handle coordinate mode
                if (cleanLine.Contains("G90"))
                    _absoluteMode = true;
                else if (cleanLine.Contains("G91"))
                    _absoluteMode = false;

                // Calculate new position
                Point3D newPosition = _currentPosition;

                if (x.HasValue)
                    newPosition.X = _absoluteMode ? x.Value : _currentPosition.X + x.Value;
                if (y.HasValue)
                    newPosition.Y = _absoluteMode ? y.Value : _currentPosition.Y + y.Value;
                if (z.HasValue)
                    newPosition.Z = _absoluteMode ? z.Value : _currentPosition.Z + z.Value;

                // Create segment if there's movement
                if (movementType != GCodeMovementType.Unknown &&
                    (newPosition.X != _currentPosition.X || newPosition.Y != _currentPosition.Y || newPosition.Z != _currentPosition.Z))
                {
                    var segment = new GCodeSegment
                    {
                        StartPoint = _currentPosition,
                        EndPoint = newPosition,
                        MovementType = movementType,
                        FeedRate = _currentFeedRate, // 🆕 Use modal feed rate
                        LineNumber = lineNumber,
                        OriginalCode = line
                    };

                    // 🆕 Add arc parameters for proper arc length calculation
                    if (movementType == GCodeMovementType.Arc)
                    {
                        segment.ArcCenterI = i ?? 0;
                        segment.ArcCenterJ = j ?? 0;
                        segment.ArcCenterK = k ?? 0;
                        segment.ArcRadius = r;
                        segment.IsClockwise = isClockwise;
                    }

                    segments.Add(segment);

                    // 🆕 Debug logging for development
                    if (lineNumber % 50 == 0) // Log every 50th line to avoid spam
                    {
                        double distance = CalculateSegmentDistance(segment);
                        System.Diagnostics.Debug.WriteLine(
                            $"Line {lineNumber}: {movementType}, Distance: {distance:F2}mm, " +
                            $"FeedRate: {_currentFeedRate}mm/min");
                    }
                }

                // Update current position
                _currentPosition = newPosition;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ⚠️ Parse error at line {lineNumber}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset parser state for new file
        /// </summary>
        public void Reset()
        {
            _currentPosition = new Point3D(0, 0, 0);
            _absoluteMode = true;
            _currentFeedRate = 0; // 🆕 Reset modal feed rate
        }

        /// <summary>
        /// 🆕 Calculate accurate distance for a segment (including proper arc length)
        /// </summary>
        public double CalculateSegmentDistance(GCodeSegment segment)
        {
            switch (segment.MovementType)
            {
                case GCodeMovementType.Linear:
                case GCodeMovementType.Rapid:
                    return CalculateLinearDistance(segment.StartPoint, segment.EndPoint);

                case GCodeMovementType.Arc:
                    return CalculateArcDistance(segment);

                default:
                    return 0;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Remove comments from G-code line
        /// </summary>
        /// <param name="line">G-code line</param>
        /// <returns>Line without comments</returns>
        private string RemoveComments(string line)
        {
            // Remove semicolon comments
            int semicolonIndex = line.IndexOf(';');
            if (semicolonIndex >= 0)
                line = line.Substring(0, semicolonIndex);

            // Remove parentheses comments
            int openParen = line.IndexOf('(');
            if (openParen >= 0)
            {
                int closeParen = line.IndexOf(')', openParen);
                if (closeParen >= 0)
                    line = line.Remove(openParen, closeParen - openParen + 1);
            }

            return line.Trim();
        }

        /// <summary>
        /// Extract coordinate value from G-code line
        /// </summary>
        /// <param name="line">G-code line</param>
        /// <param name="axis">Axis letter (X, Y, Z, F, etc.)</param>
        /// <returns>Coordinate value or null if not found</returns>
        private double? ExtractCoordinate(string line, char axis)
        {
            try
            {
                var pattern = $@"{axis}([-+]?\d*\.?\d+)";
                var match = Regex.Match(line, pattern);

                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 🆕 Calculate 3D linear distance between two points
        /// </summary>
        private double CalculateLinearDistance(Point3D start, Point3D end)
        {
            double deltaX = end.X - start.X;
            double deltaY = end.Y - start.Y;
            double deltaZ = end.Z - start.Z;

            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
        }

        /// <summary>
        /// 🆕 Calculate arc length for G02/G03 commands
        /// </summary>
        private double CalculateArcDistance(GCodeSegment segment)
        {
            try
            {
                // If radius is specified (R parameter), use it
                if (segment.ArcRadius.HasValue)
                {
                    return CalculateArcDistanceFromRadius(segment);
                }
                
                // Otherwise use I,J,K center offsets
                return CalculateArcDistanceFromCenter(segment);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Arc calculation error: {ex.Message}");
                // Fallback to linear distance
                return CalculateLinearDistance(segment.StartPoint, segment.EndPoint);
            }
        }

        /// <summary>
        /// 🆕 Calculate arc distance using radius (R parameter)
        /// Positive R = minor arc (less than 180°), Negative R = major arc (more than 180°)
        /// </summary>
        private double CalculateArcDistanceFromRadius(GCodeSegment segment)
        {
            double radius = Math.Abs(segment.ArcRadius.Value);
            bool isMajorArc = segment.ArcRadius.Value < 0; // Negative R means major arc
            
            // Calculate chord length (straight line distance in XY plane)
            double dx = segment.EndPoint.X - segment.StartPoint.X;
            double dy = segment.EndPoint.Y - segment.StartPoint.Y;
            double dz = segment.EndPoint.Z - segment.StartPoint.Z;
            double chordLengthXY = Math.Sqrt(dx * dx + dy * dy);
            
            if (chordLengthXY < 0.0001)
            {
                // Full circle (start = end in XY, but may have Z movement = helix)
                double fullCircleLength = 2 * Math.PI * radius;
                // Add helical component
                return Math.Sqrt(fullCircleLength * fullCircleLength + dz * dz);
            }
            
            if (chordLengthXY > 2 * radius)
            {
                // Invalid: chord cannot be longer than diameter, fallback to linear
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            
            // Calculate central angle using chord length and radius
            // chord = 2 * radius * sin(angle/2)
            // angle = 2 * arcsin(chord / (2 * radius))
            double halfAngle = Math.Asin(chordLengthXY / (2 * radius));
            double centralAngle = 2 * halfAngle;
            
            // If major arc, use the larger angle
            if (isMajorArc)
            {
                centralAngle = 2 * Math.PI - centralAngle;
            }
            
            // 2D Arc length
            double arcLength2D = radius * centralAngle;
            
            // For helical arcs, include Z movement (helix)
            // Total length = sqrt(arcLength2D^2 + dz^2) using helix formula
            if (Math.Abs(dz) > 0.0001)
            {
                // Helix: length = sqrt((2*pi*r*turns)^2 + height^2) but for partial arc
                // Simplified: treat as hypotenuse of arc length and Z travel
                return Math.Sqrt(arcLength2D * arcLength2D + dz * dz);
            }
            
            return arcLength2D;
        }

        /// <summary>
        /// 🆕 Calculate arc distance using center offsets (I,J,K parameters)
        /// </summary>
        private double CalculateArcDistanceFromCenter(GCodeSegment segment)
        {
            // Calculate center point (I,J,K are offsets from start point)
            Point3D center = new Point3D(
                segment.StartPoint.X + segment.ArcCenterI,
                segment.StartPoint.Y + segment.ArcCenterJ,
                segment.StartPoint.Z + segment.ArcCenterK
            );

            // Calculate radius from start point to center (in XY plane)
            double dx1 = segment.StartPoint.X - center.X;
            double dy1 = segment.StartPoint.Y - center.Y;
            double radius = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            
            if (radius < 0.0001)
            {
                // Invalid radius, fallback to linear distance
                return CalculateLinearDistance(segment.StartPoint, segment.EndPoint);
            }
            
            // Calculate start and end angles
            double startAngle = Math.Atan2(
                segment.StartPoint.Y - center.Y,
                segment.StartPoint.X - center.X
            );
            
            double endAngle = Math.Atan2(
                segment.EndPoint.Y - center.Y,
                segment.EndPoint.X - center.X
            );

            // Calculate angular difference
            double angularDiff = endAngle - startAngle;
            
            // Check for full circle (start point ≈ end point in XY)
            double xyDistance = Math.Sqrt(
                Math.Pow(segment.EndPoint.X - segment.StartPoint.X, 2) +
                Math.Pow(segment.EndPoint.Y - segment.StartPoint.Y, 2)
            );
            
            if (xyDistance < 0.0001)
            {
                // Full circle
                angularDiff = 2 * Math.PI;
            }
            else
            {
                // Normalize angle based on direction
                if (segment.IsClockwise)
                {
                    // CW: angle should be negative
                    while (angularDiff > 0) angularDiff -= 2 * Math.PI;
                    if (angularDiff > -0.0001) angularDiff = -2 * Math.PI; // Avoid zero for near-full circle
                }
                else
                {
                    // CCW: angle should be positive
                    while (angularDiff < 0) angularDiff += 2 * Math.PI;
                    if (angularDiff < 0.0001) angularDiff = 2 * Math.PI; // Avoid zero for near-full circle
                }
            }

            // 2D Arc length = radius * |angle|
            double arcLength2D = radius * Math.Abs(angularDiff);
            
            // For helical arcs, include Z movement
            double dz = segment.EndPoint.Z - segment.StartPoint.Z;
            if (Math.Abs(dz) > 0.0001)
            {
                // Helix length
                return Math.Sqrt(arcLength2D * arcLength2D + dz * dz);
            }
            
            return arcLength2D;
        }

        #endregion
    }
}