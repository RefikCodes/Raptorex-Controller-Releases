using System;
using System.Globalization;
using System.Linq;

namespace CncControlApp.Helpers
{
    /// <summary>
    /// Machine Limits Helper - Calculates safe movement distances based on machine limits
    /// Works with both GRBL $130/$131/$132 and FluidNC settings
    /// 
    /// GRBL/FluidNC Coordinate System (typical):
    /// - After homing: Machine coordinates are at negative max (e.g., X=-300, Y=-400, Z=-100)
    /// - Home position is at the NEGATIVE limit (most negative point)
    /// - Z=0 is typically at the TOP (home position for Z)
    /// - Moving "up" in Z means going towards 0 (less negative / more positive)
    /// - $130/$131/$132 define max travel as positive values
    /// </summary>
    public static class MachineLimitsHelper
    {
        private const double SafetyMargin = 2.0; // mm safety buffer from limits
        private const double MinMoveDistance = 0.5; // Minimum movement to attempt
        
        /// <summary>
        /// Get axis max travel from settings ($130=X, $131=Y, $132=Z)
        /// </summary>
        public static double GetAxisMaxTravel(char axis, MainControll controller)
        {
            if (controller?.Settings == null) return GetDefaultMaxTravel(axis);
            
            int settingId = 0;
            switch (axis)
            {
                case 'X': settingId = 130; break;
                case 'Y': settingId = 131; break;
                case 'Z': settingId = 132; break;
                default: return GetDefaultMaxTravel(axis);
            }
            
            var setting = controller.Settings.FirstOrDefault(s => s.Id == settingId);
            if (setting != null && double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxTravel))
            {
                return Math.Abs(maxTravel); // Always positive
            }
            
            return GetDefaultMaxTravel(axis);
        }
        
        private static double GetDefaultMaxTravel(char axis)
        {
            switch (axis)
            {
                case 'X': return 300.0;
                case 'Y': return 400.0;
                case 'Z': return 100.0;
                default: return 100.0;
            }
        }
        
        /// <summary>
        /// Get current machine position for axis
        /// </summary>
        public static double GetAxisMachinePosition(char axis, MainControll controller)
        {
            if (controller?.MStatus == null) return 0.0;
            
            switch (axis)
            {
                case 'X': return controller.MStatus.X;
                case 'Y': return controller.MStatus.Y;
                case 'Z': return controller.MStatus.Z;
                default: return 0.0;
            }
        }
        
        /// <summary>
        /// Calculate safe retract distance for an axis in the given direction
        /// Direction: +1 = positive direction (towards 0/home), -1 = negative direction (away from home)
        /// 
        /// For Z axis (typical setup):
        /// - Machine Z is negative (e.g., -50)
        /// - Home (Z=0) is at the TOP
        /// - Retract UP means moving towards Z=0 (positive direction)
        /// - Room to retract = |machineZ| - safetyMargin
        /// </summary>
        public static double GetSafeRetractDistance(char axis, int direction, double requestedDistance, MainControll controller)
        {
            double machinePos = GetAxisMachinePosition(axis, controller);
            double maxTravel = GetAxisMaxTravel(axis, controller);
            
            // Calculate room to move in the requested direction
            double roomToMove;
            
            if (direction > 0)
            {
                // Moving in positive direction (towards 0 or positive limit)
                // For typical GRBL: machinePos is negative, room = |machinePos| (distance to 0)
                if (machinePos < 0)
                {
                    roomToMove = Math.Abs(machinePos);
                }
                else
                {
                    // Already at or past 0, limited room on positive side
                    roomToMove = Math.Max(0, maxTravel - machinePos);
                }
            }
            else
            {
                // Moving in negative direction (away from 0, towards negative limit)
                // Room = maxTravel - |machinePos| (if machinePos is negative)
                if (machinePos < 0)
                {
                    roomToMove = maxTravel - Math.Abs(machinePos);
                }
                else
                {
                    roomToMove = maxTravel + machinePos;
                }
            }
            
            // Apply safety margin
            double safeRoom = Math.Max(0, roomToMove - SafetyMargin);
            
            // Calculate final safe distance
            double safeDistance = Math.Min(requestedDistance, safeRoom);
            
            // Ensure minimum distance if there's any room
            if (safeDistance < MinMoveDistance && safeRoom >= MinMoveDistance)
            {
                safeDistance = MinMoveDistance;
            }
            
            // Log if we're limiting the movement
            if (safeDistance < requestedDistance - 0.1 && controller != null)
            {
                controller.AddLogMessage($"> ⚠️ {axis} hareket sınırlandı: {requestedDistance:F1}mm → {safeDistance:F1}mm (Machine {axis}={machinePos:F2}, limit yakın)");
            }
            
            return Math.Max(0, safeDistance);
        }
        
        /// <summary>
        /// Calculate safe movement distance for any axis and direction
        /// </summary>
        public static double GetSafeMoveDistance(char axis, int direction, double requestedDistance, MainControll controller)
        {
            return GetSafeRetractDistance(axis, direction, requestedDistance, controller);
        }
        
        /// <summary>
        /// Check if there's enough room to move in the specified direction
        /// </summary>
        public static bool HasRoomToMove(char axis, int direction, double requiredDistance, MainControll controller)
        {
            double safeDistance = GetSafeRetractDistance(axis, direction, requiredDistance, controller);
            return safeDistance >= requiredDistance * 0.9; // Allow 10% tolerance
        }
        
        /// <summary>
        /// Get warning message if approaching limits
        /// </summary>
        public static string GetLimitWarning(char axis, int direction, MainControll controller)
        {
            double machinePos = GetAxisMachinePosition(axis, controller);
            double maxTravel = GetAxisMaxTravel(axis, controller);
            
            double roomToMove;
            string limitName;
            
            if (direction > 0)
            {
                roomToMove = machinePos < 0 ? Math.Abs(machinePos) : Math.Max(0, maxTravel - machinePos);
                limitName = "üst/pozitif";
            }
            else
            {
                roomToMove = machinePos < 0 ? (maxTravel - Math.Abs(machinePos)) : (maxTravel + machinePos);
                limitName = "alt/negatif";
            }
            
            if (roomToMove < SafetyMargin * 2)
            {
                return $"{axis} ekseni {limitName} limite çok yakın! (Kalan: {roomToMove:F1}mm)";
            }
            
            return null;
        }
        
        /// <summary>
        /// Get all axis limit information for diagnostics
        /// </summary>
        public static string GetLimitDiagnostics(MainControll controller)
        {
            if (controller?.MStatus == null) return "Machine status not available";
            
            var xMax = GetAxisMaxTravel('X', controller);
            var yMax = GetAxisMaxTravel('Y', controller);
            var zMax = GetAxisMaxTravel('Z', controller);
            
            var xPos = GetAxisMachinePosition('X', controller);
            var yPos = GetAxisMachinePosition('Y', controller);
            var zPos = GetAxisMachinePosition('Z', controller);
            
            return $"Limits: X={xMax:F0}mm (pos={xPos:F1}), Y={yMax:F0}mm (pos={yPos:F1}), Z={zMax:F0}mm (pos={zPos:F1})";
        }
    }
}
