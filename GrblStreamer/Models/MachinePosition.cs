namespace GrblStreamer.Models
{
    /// <summary>
    /// Makine pozisyon bilgileri
    /// </summary>
    public class MachinePosition
    {
        /// <summary>X ekseni pozisyonu (mm)</summary>
        public double X { get; set; }
        
        /// <summary>Y ekseni pozisyonu (mm)</summary>
        public double Y { get; set; }
        
        /// <summary>Z ekseni pozisyonu (mm)</summary>
        public double Z { get; set; }
        
        /// <summary>A ekseni pozisyonu (derece) - 4. eksen varsa</summary>
        public double A { get; set; }

        public override string ToString()
        {
            return string.Format("X:{0:F3} Y:{1:F3} Z:{2:F3} A:{3:F3}", X, Y, Z, A);
        }
    }

    /// <summary>
    /// Work Coordinate Offset değerleri
    /// </summary>
    public class WorkOffset
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double A { get; set; }

        public override string ToString()
        {
            return string.Format("WCO X:{0:F3} Y:{1:F3} Z:{2:F3}", X, Y, Z);
        }
    }
}
