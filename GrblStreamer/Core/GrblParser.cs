using System;
using System.Linq;
using System.Text.RegularExpressions;
using GrblStreamer.Enums;
using GrblStreamer.Models;

namespace GrblStreamer.Core
{
    /// <summary>
    /// GRBL yanıtlarını parse eden sınıf
    /// </summary>
    public static class GrblParser
    {
        // Regex patterns
        private static readonly Regex StatusRegex = new Regex(@"<([^,|]+)[,|]", RegexOptions.Compiled);
        private static readonly Regex MPosRegex = new Regex(@"MPos:([-\d.]+),([-\d.]+),([-\d.]+)(?:,([-\d.]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WPosRegex = new Regex(@"WPos:([-\d.]+),([-\d.]+),([-\d.]+)(?:,([-\d.]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WCORegex = new Regex(@"WCO:([-\d.]+),([-\d.]+),([-\d.]+)(?:,([-\d.]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BufferRegex = new Regex(@"Bf:(\d+),(\d+)", RegexOptions.Compiled);
        private static readonly Regex OverrideRegex = new Regex(@"Ov:(\d+),(\d+),(\d+)", RegexOptions.Compiled);
        private static readonly Regex FeedSpindleRegex = new Regex(@"FS:(\d+),(\d+)", RegexOptions.Compiled);
        private static readonly Regex PinsRegex = new Regex(@"Pn:([XYZPDHRS]+)", RegexOptions.Compiled);
        private static readonly Regex ErrorRegex = new Regex(@"error:(\d+)", RegexOptions.Compiled);
        private static readonly Regex AlarmRegex = new Regex(@"ALARM:(\d+)", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new Regex(@"\[VER:([\d.]+)\.(\d{8}):?([^\]]*)\]", RegexOptions.Compiled);
        private static readonly Regex OptionsRegex = new Regex(@"\[OPT:([^\]]+)\]", RegexOptions.Compiled);

        /// <summary>
        /// GRBL durum mesajını parse eder (< > arasındaki)
        /// </summary>
        public static GrblStatus ParseStatus(string data)
        {
            var status = new GrblStatus { RawStatus = data };

            // State
            var stateMatch = StatusRegex.Match(data);
            if (stateMatch.Success)
            {
                var stateStr = stateMatch.Groups[1].Value.Split(':')[0];
                status.State = ParseState(stateStr);
            }

            // MPos
            var mposMatch = MPosRegex.Match(data);
            if (mposMatch.Success)
            {
                status.MachinePosition = new MachinePosition
                {
                    X = double.Parse(mposMatch.Groups[1].Value),
                    Y = double.Parse(mposMatch.Groups[2].Value),
                    Z = double.Parse(mposMatch.Groups[3].Value),
                    A = mposMatch.Groups[4].Success ? double.Parse(mposMatch.Groups[4].Value) : 0
                };
            }

            // WPos
            var wposMatch = WPosRegex.Match(data);
            if (wposMatch.Success)
            {
                status.WorkPosition = new MachinePosition
                {
                    X = double.Parse(wposMatch.Groups[1].Value),
                    Y = double.Parse(wposMatch.Groups[2].Value),
                    Z = double.Parse(wposMatch.Groups[3].Value),
                    A = wposMatch.Groups[4].Success ? double.Parse(wposMatch.Groups[4].Value) : 0
                };
            }

            // WCO
            var wcoMatch = WCORegex.Match(data);
            if (wcoMatch.Success)
            {
                status.WorkOffset = new WorkOffset
                {
                    X = double.Parse(wcoMatch.Groups[1].Value),
                    Y = double.Parse(wcoMatch.Groups[2].Value),
                    Z = double.Parse(wcoMatch.Groups[3].Value),
                    A = wcoMatch.Groups[4].Success ? double.Parse(wcoMatch.Groups[4].Value) : 0
                };
            }

            // Buffer
            var bufferMatch = BufferRegex.Match(data);
            if (bufferMatch.Success)
            {
                status.PlannerBuffer = int.Parse(bufferMatch.Groups[1].Value);
                status.RxBuffer = int.Parse(bufferMatch.Groups[2].Value);
            }

            // Overrides
            var overrideMatch = OverrideRegex.Match(data);
            if (overrideMatch.Success)
            {
                status.FeedOverride = int.Parse(overrideMatch.Groups[1].Value);
                status.RapidOverride = int.Parse(overrideMatch.Groups[2].Value);
                status.SpindleOverride = int.Parse(overrideMatch.Groups[3].Value);
            }

            // Feed & Spindle
            var fsMatch = FeedSpindleRegex.Match(data);
            if (fsMatch.Success)
            {
                status.FeedRate = double.Parse(fsMatch.Groups[1].Value);
                status.SpindleSpeed = int.Parse(fsMatch.Groups[2].Value);
            }

            // Input Pins
            var pinsMatch = PinsRegex.Match(data);
            if (pinsMatch.Success)
            {
                status.InputPins = pinsMatch.Groups[1].Value.ToCharArray().ToList();
            }

            return status;
        }

        /// <summary>
        /// Makine durumunu parse eder
        /// </summary>
        public static GrblState ParseState(string state)
        {
            switch (state.ToLower())
            {
                case "idle":
                    return GrblState.Idle;
                case "run":
                    return GrblState.Run;
                case "hold":
                case "hold:0":
                case "hold:1":
                    return GrblState.Hold;
                case "jog":
                    return GrblState.Jog;
                case "alarm":
                    return GrblState.Alarm;
                case "door":
                case "door:0":
                case "door:1":
                case "door:2":
                case "door:3":
                    return GrblState.Door;
                case "check":
                    return GrblState.Check;
                case "home":
                    return GrblState.Home;
                case "sleep":
                    return GrblState.Sleep;
                default:
                    return GrblState.Unknown;
            }
        }

        /// <summary>
        /// Error kodunu parse eder
        /// </summary>
        public static Tuple<int, string> ParseError(string data)
        {
            var match = ErrorRegex.Match(data);
            if (match.Success)
            {
                var code = int.Parse(match.Groups[1].Value);
                return Tuple.Create(code, GrblStrings.GetErrorMessage(code));
            }
            return null;
        }

        /// <summary>
        /// Alarm kodunu parse eder
        /// </summary>
        public static Tuple<int, string> ParseAlarm(string data)
        {
            var match = AlarmRegex.Match(data);
            if (match.Success)
            {
                var code = int.Parse(match.Groups[1].Value);
                return Tuple.Create(code, GrblStrings.GetAlarmMessage(code));
            }
            return null;
        }

        /// <summary>
        /// Firmware versiyon bilgisini parse eder
        /// </summary>
        public static FirmwareInfo ParseVersion(string data)
        {
            var match = VersionRegex.Match(data);
            if (match.Success)
            {
                var info = new FirmwareInfo
                {
                    Version = match.Groups[1].Value,
                    BuildDate = match.Groups[2].Value,
                    Options = match.Groups[3].Value
                };

                // Firmware türünü belirle
                if (data.IndexOf("grblHAL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    info.Type = FirmwareType.GrblHAL;
                    info.Platform = "grblHAL";
                    info.RxBufferSize = 1023;
                }
                else if (data.IndexOf("FluidNC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    info.Type = FirmwareType.FluidNC;
                    info.Platform = "FluidNC";
                }
                else
                {
                    info.Type = FirmwareType.Grbl;
                    info.Platform = "grbl";
                    info.RxBufferSize = 127;
                }

                return info;
            }
            return null;
        }

        /// <summary>
        /// Yanıtın OK olup olmadığını kontrol eder
        /// </summary>
        public static bool IsOk(string data)
        {
            return data.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Yanıtın durum mesajı olup olmadığını kontrol eder
        /// </summary>
        public static bool IsStatus(string data)
        {
            return data.StartsWith("<") && data.Contains(">");
        }

        /// <summary>
        /// Yanıtın hata olup olmadığını kontrol eder
        /// </summary>
        public static bool IsError(string data)
        {
            return data.StartsWith("error:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Yanıtın alarm olup olmadığını kontrol eder
        /// </summary>
        public static bool IsAlarm(string data)
        {
            return data.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Yanıtın mesaj olup olmadığını kontrol eder
        /// </summary>
        public static bool IsMessage(string data)
        {
            return data.StartsWith("[MSG:");
        }
    }
}
