namespace GrblStreamer.Enums
{
    /// <summary>
    /// GRBL makine durumları
    /// </summary>
    public enum GrblState
    {
        Unknown,
        Idle,
        Run,
        Hold,
        Jog,
        Alarm,
        Door,
        Check,
        Home,
        Sleep
    }
}
