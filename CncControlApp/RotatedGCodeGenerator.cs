using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CncControlApp
{
    internal static class RotatedGCodeGenerator
    {
        #region Rotation Outcome Model
        internal sealed class RotationOutcome
        {
            // Nihai çıktı satırları
            public List<string> Lines { get; set; } = new List<string>();

            // Kaynak (rotasyon öncesi, orijinal G-Code) bounding box
            public double SourceMinX { get; set; }
            public double SourceMinY { get; set; }
            public double SourceMaxX { get; set; }
            public double SourceMaxY { get; set; }

            // Döndürülmüş fakat fit (normalize) uygulanmadan önce bounding box
            public double RotatedBeforeFitMinX { get; set; }
            public double RotatedBeforeFitMinY { get; set; }
            public double RotatedBeforeFitMaxX { get; set; }
            public double RotatedBeforeFitMaxY { get; set; }

            // Fit (normalizeToPositive) uygulandıktan sonra bounding box (genellikle 0'dan başlar)
            public double RotatedAfterFitMinX { get; set; }
            public double RotatedAfterFitMinY { get; set; }
            public double RotatedAfterFitMaxX { get; set; }
            public double RotatedAfterFitMaxY { get; set; }

            // Fit sırasında uygulanan kaydırma
            public double AppliedShiftX { get; set; }
            public double AppliedShiftY { get; set; }

            // Kullanılan pivot
            public RotationPivotMode PivotMode { get; set; }
            public double PivotX { get; set; }
            public double PivotY { get; set; }

            // 90° yön (CW = true)
            public bool Clockwise { get; set; }

            // İsteğe bağlı debug log
            public List<RotationLogEntry> DebugLog { get; set; }

            public string GenerateG92Header(double machineSpindleAtX, double machineSpindleAtY, int decimals = 3)
            {
                string fx = AppliedShiftX != 0 ? $"(AppliedShiftX={AppliedShiftX.ToString("0.###", CultureInfo.InvariantCulture)}) " : "";
                string fy = AppliedShiftY != 0 ? $"(AppliedShiftY={AppliedShiftY.ToString("0.###", CultureInfo.InvariantCulture)}) " : "";
                var sb = new StringBuilder();
                sb.AppendLine("(--- WORK OFFSET SET (G92) ---)");
                sb.AppendLine($"(SpindleMachinePos X={machineSpindleAtX:0.###} Y={machineSpindleAtY:0.###})");
                sb.AppendLine($"{fx}{fy}");
                sb.AppendLine("G92 X0 Y0");
                sb.AppendLine("(--- END WORK OFFSET SET ---)");
                return sb.ToString();
            }
        }
        #endregion

        // Extended: allow numbers like .5 and - .5 (leading dot), still simple (no exponent).
        private static readonly Regex WordRegex = new Regex(@"([A-Za-z])([+\-]?(?:\d+(?:\.\d+)?|\.\d+))",
            RegexOptions.Compiled);

        private static readonly Regex G90Regex = new Regex(@"\bG90\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex G91Regex = new Regex(@"\bG91\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex G901Regex = new Regex(@"\bG90\.1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex G911Regex = new Regex(@"\bG91\.1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ArcCWRegex = new Regex(@"\bG0*2\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ArcCCWRegex = new Regex(@"\bG0*3\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const bool AUTO_SAVE_DESKTOP_LOG = true;
        private const bool AUTO_SAVE_DESKTOP_ROTATED_GCODE = true;
        private const string ROTATED_GCODE_BASENAME = "rotated";
        private const string ROTATED_GCODE_EXTENSION = ".gcode";

        // File write suppression: keep code paths but allow disabling at runtime.
        private static bool SuppressFileWrites = true;
        public static void EnableFileWrites()  => SuppressFileWrites = false;
        public static void DisableFileWrites() => SuppressFileWrites = true;

        public enum RotationPivotMode
        {
            Origin,
            BoundingBoxMin,
            BoundingBoxCenter,
            Custom
        }

        public class RotationLogEntry
        {
            public int LineNumber;
            public string Original;
            public string Rotated;
            public bool IsArc;
            public bool LinearAbsoluteBefore;
            public bool LinearAbsoluteAfter;
            public bool ArcCenterAbsoluteMode;
            public double? OrigX, OrigY, OrigI, OrigJ;
            public double? SourceXUsed, SourceYUsed;
            public double? RotX, RotY, RotI, RotJ;
            public bool CommentOnly;
            public List<string> Warnings = new List<string>();
        }

        public static List<string> GenerateRotatedQuarterTurns(
            IEnumerable<string> originalLines,
            int quarterTurns,
            bool normalizeToPositive = false,
            int decimals = 3,
            List<RotationLogEntry> debugLog = null,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            double? customPivotX = null,
            double? customPivotY = null,
            bool clockwise = false)
        {
            // Normalize quarter turns (CCW positive). If clockwise requested, invert direction.
            quarterTurns = ((quarterTurns % 4) + 4) % 4;
            if (clockwise)
            {
                // A clockwise 90 is equivalent to 3 CCW quarter turns, etc.
                if (quarterTurns != 0)
                    quarterTurns = (4 - quarterTurns) % 4;
            }
            if (quarterTurns == 0)
                return originalLines?.ToList() ?? new List<string>();

            var result = new List<string>();
            if (originalLines == null) return result;

            double pivotX = 0, pivotY = 0;
            if (pivotMode != RotationPivotMode.Origin)
            {
                var bbox = ComputeBoundingBox(originalLines);
                if (pivotMode == RotationPivotMode.BoundingBoxMin)
                {
                    pivotX = bbox.minX;
                    pivotY = bbox.minY;
                }
                else if (pivotMode == RotationPivotMode.BoundingBoxCenter)
                {
                    pivotX = (bbox.minX + bbox.maxX) * 0.5;
                    pivotY = (bbox.minY + bbox.maxY) * 0.5;
                }
                else if (pivotMode == RotationPivotMode.Custom)
                {
                    pivotX = customPivotX ?? 0;
                    pivotY = customPivotY ?? 0;
                }
            }

            bool linearAbsoluteMode = true;
            bool arcCenterAbsoluteMode = false;
            double lastSrcAbsX = 0, lastSrcAbsY = 0;

            var absCoordsForNormalization = new List<(int index, double x, double y, bool hasX, bool hasY)>();
            var internalLog = debugLog ?? new List<RotationLogEntry>();

            int lineNo = -1;
            foreach (var raw in originalLines)
            {
                lineNo++;

                ExtractCommentWithParentheses(raw,
                    out string codePart,
                    out string commentPart,
                    out bool commentOnly);

                if (commentOnly || string.IsNullOrWhiteSpace(codePart))
                {
                    result.Add(raw);
                    if (debugLog != null)
                    {
                        internalLog.Add(new RotationLogEntry
                        {
                            LineNumber = lineNo + 1,
                            Original = raw,
                            Rotated = raw,
                            CommentOnly = true,
                            LinearAbsoluteBefore = linearAbsoluteMode,
                            LinearAbsoluteAfter = linearAbsoluteMode
                        });
                    }
                    continue;
                }

                string upper = codePart.ToUpperInvariant();
                bool linearAbsBefore = linearAbsoluteMode;

                if (G90Regex.IsMatch(upper) && !G901Regex.IsMatch(upper)) linearAbsoluteMode = true;
                if (G91Regex.IsMatch(upper) && !G911Regex.IsMatch(upper)) linearAbsoluteMode = false;
                if (G901Regex.IsMatch(upper)) arcCenterAbsoluteMode = true;
                if (G911Regex.IsMatch(upper)) arcCenterAbsoluteMode = false;

                bool isArc = ArcCWRegex.IsMatch(upper) || ArcCCWRegex.IsMatch(upper);

                var tokens = WordRegex.Matches(codePart)
                    .Cast<Match>()
                    .Select(m => (Letter: m.Groups[1].Value, ValueText: m.Groups[2].Value, Raw: m.Value, Index: m.Index))
                    .ToList();

                double? x = null, y = null, i = null, j = null;
                double? r = null;
                bool hadI = false, hadJ = false;

                foreach (var t in tokens)
                {
                    if (!double.TryParse(t.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        continue;
                    switch (t.Letter.ToUpperInvariant())
                    {
                        case "X": x = v; break;
                        case "Y": y = v; break;
                        case "I": i = v; hadI = true; break;
                        case "J": j = v; hadJ = true; break;
                        case "R": r = v; break;
                    }
                }

                bool hasAnyXY = x.HasValue || y.HasValue;
                double? newX = null, newY = null;
                double? newI = null, newJ = null;
                double? srcXUsed = null, srcYUsed = null;

                if (linearAbsoluteMode && hasAnyXY)
                {
                    double srcX = x ?? lastSrcAbsX;
                    double srcY = y ?? lastSrcAbsY;
                    (double rx, double ry) = RotateXYAroundPivot(srcX, srcY, quarterTurns, pivotX, pivotY);
                    newX = rx;
                    newY = ry;
                    srcXUsed = srcX;
                    srcYUsed = srcY;
                    lastSrcAbsX = srcX;
                    lastSrcAbsY = srcY;
                }
                else if (!linearAbsoluteMode && hasAnyXY)
                {
                    double dx = x ?? 0;
                    double dy = y ?? 0;
                    (double rdx, double rdy) = RotateXY(dx, dy, quarterTurns);
                    if (Math.Abs(rdx) > 1e-12 || x.HasValue) newX = rdx;
                    if (Math.Abs(rdy) > 1e-12 || y.HasValue) newY = rdy;
                    srcXUsed = dx;
                    srcYUsed = dy;
                }

                if (isArc && (hadI || hadJ))
                {
                    if (arcCenterAbsoluteMode)
                    {
                        double cI = i ?? 0;
                        double cJ = j ?? 0;
                        (double ri, double rj) = RotateXYAroundPivot(cI, cJ, quarterTurns, pivotX, pivotY);
                        if (hadI) newI = ri;
                        if (hadJ) newJ = rj;
                    }
                    else
                    {
                        double srcI = i ?? 0;
                        double srcJ = j ?? 0;
                        (double ri, double rj) = RotateXY(srcI, srcJ, quarterTurns);
                        if (hadI || Math.Abs(ri) > 1e-12) newI = ri;
                        if (hadJ || Math.Abs(rj) > 1e-12) newJ = rj;
                    }
                }

                bool needPair = linearAbsoluteMode && (x.HasValue ^ y.HasValue);
                string rebuilt = RebuildLine(tokens, newX, newY, newI, newJ, r, decimals,
                    forceEmitX: needPair && newX.HasValue,
                    forceEmitY: needPair && newY.HasValue);

                if (linearAbsoluteMode && hasAnyXY && newX.HasValue && newY.HasValue)
                {
                    bool hadXToken = x.HasValue;
                    bool hadYToken = y.HasValue;
                    absCoordsForNormalization.Add((result.Count, newX.Value, newY.Value, hadXToken, hadYToken));
                }

                string finalLine = AppendComment(rebuilt, commentPart);
                result.Add(finalLine);

                if (debugLog != null)
                {
                    var entry = new RotationLogEntry
                    {
                        LineNumber = lineNo + 1,
                        Original = raw,
                        Rotated = finalLine,
                        IsArc = isArc,
                        LinearAbsoluteBefore = linearAbsBefore,
                        LinearAbsoluteAfter = linearAbsoluteMode,
                        ArcCenterAbsoluteMode = arcCenterAbsoluteMode,
                        OrigX = x,
                        OrigY = y,
                        OrigI = i,
                        OrigJ = j,
                        SourceXUsed = srcXUsed,
                        SourceYUsed = srcYUsed,
                        RotX = newX,
                        RotY = newY,
                        RotI = newI,
                        RotJ = newJ
                    };

                    var unsupported = tokens
                        .Select(t => t.Letter.ToUpperInvariant())
                        .Where(l =>
                            !"XYZIJKFRSD".Contains(l) &&
                            l != "G" && l != "M" && l != "T" && l != "A" && l != "Z" && l != "P")
                        .Distinct()
                        .ToList();
                    if (unsupported.Count > 0)
                        entry.Warnings.Add("Unsupported letters: " + string.Join(",", unsupported));

                    if (Regex.IsMatch(finalLine, @"\bX0(?:\.0+)?\s+D0", RegexOptions.IgnoreCase))
                        entry.Warnings.Add("Pattern 'X0 D0' detected");

                    debugLog.Add(entry);
                }
            }

            if (normalizeToPositive)
                NormalizeAbsoluteXY(result, absCoordsForNormalization, decimals);

            if (AUTO_SAVE_DESKTOP_ROTATED_GCODE && !SuppressFileWrites && result.Count > 0)
            {
                try
                {
                    string rotatedPath = GetNextDesktopRotatedGCodePath();
                    File.WriteAllText(rotatedPath, string.Join(Environment.NewLine, result) + Environment.NewLine, Encoding.UTF8);
                    App.MainController?.AddLogMessage($"> Rotated G-Code saved: {rotatedPath}");
                }
                catch (Exception ex)
                {
                    App.MainController?.AddLogMessage($"> Rotated G-Code save error: {ex.Message}");
                }
            }

            if (AUTO_SAVE_DESKTOP_LOG && !SuppressFileWrites && internalLog.Count > 0)
            {
                try
                {
                    string path = GetNextDesktopRotationLogPath();
                    RotationLogExporter.WriteRotationLog(path, internalLog);
                    App.MainController?.AddLogMessage($"> Rotation log saved: {path}");
                }
                catch (Exception ex)
                {
                    App.MainController?.AddLogMessage($"> Rotation log save error: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 90° CW/CCW döndür, isteğe bağlı fit uygula ve tüm bounding box + shift bilgilerini döndür.
        /// Tek seferde karar verebilmek için metadata sağlar.
        /// </summary>
        public static RotationOutcome RotateWithOutcome(
            IEnumerable<string> originalLines,
            bool clockwise,
            bool fitToPositive,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            double? customPivotX = null,
            double? customPivotY = null,
            int decimals = 3,
            List<RotationLogEntry> externalLog = null)
        {
            var srcList = originalLines?.ToList() ?? new List<string>();
            // Kaynak bounding box
            var srcBox = ComputeBoundingBox(srcList);

            // Önce fitToPositive=false (geçici) çalıştır -> RotatedBeforeFit bounding box'ını hesaplamak için
            var tempLog = externalLog ?? new List<RotationLogEntry>();
            var rotatedRaw = GenerateRotatedQuarterTurns(
                srcList,
                quarterTurns: 1,
                normalizeToPositive: false,
                decimals: decimals,
                debugLog: tempLog,
                pivotMode: pivotMode,
                customPivotX: customPivotX,
                customPivotY: customPivotY,
                clockwise: clockwise);

            // Döndürülmüş (fit öncesi) bounding box
            var rotBefore = ComputeBoundingBox(rotatedRaw);

            // Eğer fit isteniyorsa ikinci kez normalizeToPositive:true ile üret
            List<string> finalLines;
            double shiftX = 0, shiftY = 0;
            var rotAfter = rotBefore;
            if (fitToPositive)
            {
                finalLines = GenerateRotatedQuarterTurns(
                    srcList,
                    quarterTurns: 1,
                    normalizeToPositive: true,
                    decimals: decimals,
                    debugLog: null, // ikinci log eklemek istemiyoruz
                    pivotMode: pivotMode,
                    customPivotX: customPivotX,
                    customPivotY: customPivotY,
                    clockwise: clockwise);

                rotAfter = ComputeBoundingBox(finalLines);
                // shift = after.min - before.min (after.min genelde 0)
                shiftX = rotAfter.minX - rotBefore.minX;
                shiftY = rotAfter.minY - rotBefore.minY;
            }
            else
            {
                finalLines = rotatedRaw;
            }

            return new RotationOutcome
            {
                Lines = finalLines,
                SourceMinX = srcBox.minX,
                SourceMinY = srcBox.minY,
                SourceMaxX = srcBox.maxX,
                SourceMaxY = srcBox.maxY,
                RotatedBeforeFitMinX = rotBefore.minX,
                RotatedBeforeFitMinY = rotBefore.minY,
                RotatedBeforeFitMaxX = rotBefore.maxX,
                RotatedBeforeFitMaxY = rotBefore.maxY,
                RotatedAfterFitMinX = rotAfter.minX,
                RotatedAfterFitMinY = rotAfter.minY,
                RotatedAfterFitMaxX = rotAfter.maxX,
                RotatedAfterFitMaxY = rotAfter.maxY,
                AppliedShiftX = shiftX,
                AppliedShiftY = shiftY,
                PivotMode = pivotMode,
                PivotX = pivotMode == RotationPivotMode.Custom ? (customPivotX ?? 0) : 
                         pivotMode == RotationPivotMode.BoundingBoxCenter ? (srcBox.minX + srcBox.maxX) * 0.5 : 
                         pivotMode == RotationPivotMode.BoundingBoxMin ? srcBox.minX : 0,
                PivotY = pivotMode == RotationPivotMode.Custom ? (customPivotY ?? 0) : 
                         pivotMode == RotationPivotMode.BoundingBoxCenter ? (srcBox.minY + srcBox.maxY) * 0.5 : 
                         pivotMode == RotationPivotMode.BoundingBoxMin ? srcBox.minY : 0,
                Clockwise = clockwise,
                DebugLog = externalLog
            };
        }

        public static RotationOutcome Rotate90CWFittedOutcome(
            IEnumerable<string> lines,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            double? pivotX = null,
            double? pivotY = null,
            int decimals = 3,
            List<RotationLogEntry> log = null)
            => RotateWithOutcome(lines, clockwise: true, fitToPositive: true, pivotMode: pivotMode, customPivotX: pivotX, customPivotY: pivotY, decimals: decimals, externalLog: log);

        public static RotationOutcome Rotate90CCWFittedOutcome(
            IEnumerable<string> lines,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            double? pivotX = null,
            double? pivotY = null,
            int decimals = 3,
            List<RotationLogEntry> log = null)
            => RotateWithOutcome(lines, clockwise: false, fitToPositive: true, pivotMode: pivotMode, customPivotX: pivotX, customPivotY: pivotY, decimals: decimals, externalLog: log);

        /// <summary>
        /// Arbitrary angle rotation (in degrees) - returns outcome with shift info
        /// </summary>
        public static RotationOutcome GenerateRotatedArbitraryAngleWithOutcome(
            IEnumerable<string> originalLines,
            double angleDegrees,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            bool normalizeToPositive = false,
            double? customPivotX = null,
            double? customPivotY = null,
            int decimals = 3)
        {
            var srcList = originalLines?.ToList() ?? new List<string>();
            var srcBox = ComputeBoundingBox(srcList);

            // First pass: rotate without normalization to get raw bounds
            var rotatedRaw = GenerateRotatedArbitraryAngle(srcList, angleDegrees, pivotMode, false, customPivotX, customPivotY, decimals);
            var rotBefore = ComputeBoundingBox(rotatedRaw);

            // Second pass: apply normalization if requested
            List<string> finalLines;
            double shiftX = 0, shiftY = 0;
            var rotAfter = rotBefore;

            if (normalizeToPositive)
            {
                finalLines = GenerateRotatedArbitraryAngle(srcList, angleDegrees, pivotMode, true, customPivotX, customPivotY, decimals);
                rotAfter = ComputeBoundingBox(finalLines);
                shiftX = rotAfter.minX - rotBefore.minX;
                shiftY = rotAfter.minY - rotBefore.minY;
            }
            else
            {
                finalLines = rotatedRaw;
            }

            double actualPivotX = 0, actualPivotY = 0;
            if (pivotMode == RotationPivotMode.BoundingBoxMin)
            {
                actualPivotX = srcBox.minX;
                actualPivotY = srcBox.minY;
            }
            else if (pivotMode == RotationPivotMode.BoundingBoxCenter)
            {
                actualPivotX = (srcBox.minX + srcBox.maxX) * 0.5;
                actualPivotY = (srcBox.minY + srcBox.maxY) * 0.5;
            }
            else if (pivotMode == RotationPivotMode.Custom)
            {
                actualPivotX = customPivotX ?? 0;
                actualPivotY = customPivotY ?? 0;
            }

            return new RotationOutcome
            {
                Lines = finalLines,
                SourceMinX = srcBox.minX,
                SourceMinY = srcBox.minY,
                SourceMaxX = srcBox.maxX,
                SourceMaxY = srcBox.maxY,
                RotatedBeforeFitMinX = rotBefore.minX,
                RotatedBeforeFitMinY = rotBefore.minY,
                RotatedBeforeFitMaxX = rotBefore.maxX,
                RotatedBeforeFitMaxY = rotBefore.maxY,
                RotatedAfterFitMinX = rotAfter.minX,
                RotatedAfterFitMinY = rotAfter.minY,
                RotatedAfterFitMaxX = rotAfter.maxX,
                RotatedAfterFitMaxY = rotAfter.maxY,
                AppliedShiftX = shiftX,
                AppliedShiftY = shiftY,
                PivotMode = pivotMode,
                PivotX = actualPivotX,
                PivotY = actualPivotY,
                Clockwise = false
            };
        }

        /// <summary>
        /// Arbitrary angle rotation (in degrees) - legacy method, returns lines only
        /// </summary>
        public static List<string> GenerateRotatedArbitraryAngle(
            IEnumerable<string> originalLines,
            double angleDegrees,
            RotationPivotMode pivotMode = RotationPivotMode.Origin,
            bool normalizeToPositive = false,
            double? customPivotX = null,
            double? customPivotY = null,
            int decimals = 3)
        {
            var srcList = originalLines?.ToList() ?? new List<string>();
            if (srcList.Count == 0) return srcList;

            // Calculate pivot
            double pivotX = 0, pivotY = 0;
            if (pivotMode != RotationPivotMode.Origin)
            {
                var bbox = ComputeBoundingBox(srcList);
                if (pivotMode == RotationPivotMode.BoundingBoxMin)
                {
                    pivotX = bbox.minX;
                    pivotY = bbox.minY;
                }
                else if (pivotMode == RotationPivotMode.BoundingBoxCenter)
                {
                    pivotX = (bbox.minX + bbox.maxX) * 0.5;
                    pivotY = (bbox.minY + bbox.maxY) * 0.5;
                }
                else if (pivotMode == RotationPivotMode.Custom)
                {
                    pivotX = customPivotX ?? 0;
                    pivotY = customPivotY ?? 0;
                }
            }

            var result = new List<string>();
            bool linearAbsoluteMode = true;
            bool arcCenterAbsoluteMode = false;
            double lastSrcAbsX = 0, lastSrcAbsY = 0;

            // Convert angle to radians
            double angleRadians = angleDegrees * Math.PI / 180.0;
            double cosAngle = Math.Cos(angleRadians);
            double sinAngle = Math.Sin(angleRadians);

            var absCoordsForNormalization = new List<(int index, double x, double y, bool hasX, bool hasY)>();

            foreach (var raw in srcList)
            {
                ExtractCommentWithParentheses(raw,
                    out string codePart,
                    out string commentPart,
                    out bool commentOnly);

                if (commentOnly || string.IsNullOrWhiteSpace(codePart))
                {
                    result.Add(raw);
                    continue;
                }

                string upper = codePart.ToUpperInvariant();

                if (G90Regex.IsMatch(upper) && !G901Regex.IsMatch(upper)) linearAbsoluteMode = true;
                if (G91Regex.IsMatch(upper) && !G911Regex.IsMatch(upper)) linearAbsoluteMode = false;
                if (G901Regex.IsMatch(upper)) arcCenterAbsoluteMode = true;
                if (G911Regex.IsMatch(upper)) arcCenterAbsoluteMode = false;

                bool isArc = ArcCWRegex.IsMatch(upper) || ArcCCWRegex.IsMatch(upper);

                var tokens = WordRegex.Matches(codePart)
                    .Cast<Match>()
                    .Select(m => (Letter: m.Groups[1].Value, ValueText: m.Groups[2].Value, Raw: m.Value, Index: m.Index))
                    .ToList();

                double? x = null, y = null, i = null, j = null, r = null;
                bool hadI = false, hadJ = false;

                foreach (var t in tokens)
                {
                    if (!double.TryParse(t.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        continue;
                    switch (t.Letter.ToUpperInvariant())
                    {
                        case "X": x = v; break;
                        case "Y": y = v; break;
                        case "I": i = v; hadI = true; break;
                        case "J": j = v; hadJ = true; break;
                        case "R": r = v; break;
                    }
                }

                bool hasAnyXY = x.HasValue || y.HasValue;
                double? newX = null, newY = null;
                double? newI = null, newJ = null;

                if (linearAbsoluteMode && hasAnyXY)
                {
                    double srcX = x ?? lastSrcAbsX;
                    double srcY = y ?? lastSrcAbsY;
                    
                    double dx = srcX - pivotX;
                    double dy = srcY - pivotY;
                    double rotatedX = dx * cosAngle - dy * sinAngle;
                    double rotatedY = dx * sinAngle + dy * cosAngle;
                    
                    newX = pivotX + rotatedX;
                    newY = pivotY + rotatedY;
                    
                    lastSrcAbsX = srcX;
                    lastSrcAbsY = srcY;
                }
                else if (!linearAbsoluteMode && hasAnyXY)
                {
                    double dx = x ?? 0;
                    double dy = y ?? 0;
                    double rotatedDx = dx * cosAngle - dy * sinAngle;
                    double rotatedDy = dx * sinAngle + dy * cosAngle;
                    
                    if (Math.Abs(rotatedDx) > 1e-12 || x.HasValue) newX = rotatedDx;
                    if (Math.Abs(rotatedDy) > 1e-12 || y.HasValue) newY = rotatedDy;
                }

                if (isArc && (hadI || hadJ))
                {
                    if (arcCenterAbsoluteMode)
                    {
                        double cI = i ?? 0;
                        double cJ = j ?? 0;
                        double dx = cI - pivotX;
                        double dy = cJ - pivotY;
                        double rotatedX = dx * cosAngle - dy * sinAngle;
                        double rotatedY = dx * sinAngle + dy * cosAngle;
                        if (hadI) newI = pivotX + rotatedX;
                        if (hadJ) newJ = pivotY + rotatedY;
                    }
                    else
                    {
                        double srcI = i ?? 0;
                        double srcJ = j ?? 0;
                        double rotatedI = srcI * cosAngle - srcJ * sinAngle;
                        double rotatedJ = srcI * sinAngle + srcJ * cosAngle;
                        if (hadI || Math.Abs(rotatedI) > 1e-12) newI = rotatedI;
                        if (hadJ || Math.Abs(rotatedJ) > 1e-12) newJ = rotatedJ;
                    }
                }

                bool needPair = linearAbsoluteMode && (x.HasValue ^ y.HasValue);
                string rebuilt = RebuildLine(tokens, newX, newY, newI, newJ, r, decimals,
                    forceEmitX: needPair && newX.HasValue,
                    forceEmitY: needPair && newY.HasValue);

                if (linearAbsoluteMode && hasAnyXY && newX.HasValue && newY.HasValue)
                {
                    bool hadXToken = x.HasValue;
                    bool hadYToken = y.HasValue;
                    absCoordsForNormalization.Add((result.Count, newX.Value, newY.Value, hadXToken, hadYToken));
                }

                string finalLine = AppendComment(rebuilt, commentPart);
                result.Add(finalLine);
            }

            if (normalizeToPositive)
                NormalizeAbsoluteXY(result, absCoordsForNormalization, decimals);

            return result;
        }

        #region Private Helper Methods

        private static string GetNextDesktopRotationLogPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            for (int i = 1; i <= 999; i++)
            {
                string path = Path.Combine(desktop, $"rotation-{i:00}.txt");
                if (!File.Exists(path))
                    return path;
            }
            return Path.Combine(desktop, $"rotation-{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }

        private static string GetNextDesktopRotatedGCodePath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            for (int i = 1; i <= 999; i++)
            {
                string path = Path.Combine(desktop, $"{ROTATED_GCODE_BASENAME}-{i:00}{ROTATED_GCODE_EXTENSION}");
                if (!File.Exists(path))
                    return path;
            }
            return Path.Combine(desktop, $"{ROTATED_GCODE_BASENAME}-{DateTime.Now:yyyyMMdd_HHmmss}{ROTATED_GCODE_EXTENSION}");
        }

        private static string RebuildLine(
             List<(string Letter, string ValueText, string Raw, int Index)> tokens,
            double? newX, double? newY, double? newI, double? newJ, double? passR,
             int decimals,
             bool forceEmitX,
             bool forceEmitY)
        {
            var outSb = new StringBuilder();
            var present = new HashSet<string>(tokens.Select(t => t.Letter.ToUpperInvariant()));

            foreach (var t in tokens)
            {
                string L = t.Letter.ToUpperInvariant();
                switch (L)
                {
                    case "X":
                        outSb.Append("X").Append(Format((newX ?? ParseOr(t.ValueText)), decimals)).Append(' ');
                        break;
                    case "Y":
                        outSb.Append("Y").Append(Format((newY ?? ParseOr(t.ValueText)), decimals)).Append(' ');
                        break;
                    case "I":
                        if (newI.HasValue) outSb.Append("I").Append(Format(newI.Value, decimals)).Append(' ');
                        else outSb.Append(t.Raw).Append(' ');
                        break;
                    case "J":
                        if (newJ.HasValue) outSb.Append("J").Append(Format(newJ.Value, decimals)).Append(' ' );
                        else outSb.Append(t.Raw).Append(' ');
                        break;
                    case "R":
                        if (passR.HasValue)
                            outSb.Append("R").Append(Format(passR.Value, decimals)).Append(' ');
                        else
                            outSb.Append(t.Raw).Append(' ');
                        break;
                    default:
                        outSb.Append(t.Raw).Append(' ');
                        break;
                }
            }

            if (forceEmitX && !present.Contains("X") && newX.HasValue)
                outSb.Append("X").Append(Format(newX.Value, decimals)).Append(' ');
            if (forceEmitY && !present.Contains("Y") && newY.HasValue)
                outSb.Append("Y").Append(Format(newY.Value, decimals)).Append(' ');

            if (newI.HasValue && !present.Contains("I"))
                outSb.Append("I").Append(Format(newI.Value, decimals)).Append(' ');
            if (newJ.HasValue && !present.Contains("J"))
                outSb.Append("J").Append(Format(newJ.Value, decimals)).Append(' ');
            if (passR.HasValue && !present.Contains("R"))
                outSb.Append("R").Append(Format(passR.Value, decimals)).Append(' ');

            return outSb.ToString().TrimEnd();
        }

        private static (double rx, double ry) RotateXY(double x, double y, int quarterTurns)
        {
            switch (quarterTurns)
            {
                case 1: return (-y, x);
                case 2: return (-x, -y);
                case 3: return (y, -x);
                default: return (x, y);
            }
        }

        private static (double rx, double ry) RotateXYAroundPivot(double x, double y, int quarterTurns, double px, double py)
        {
            double dx = x - px;
            double dy = y - py;
            (double rdx, double rdy) = RotateXY(dx, dy, quarterTurns);
            return (px + rdx, py + rdy);
        }

        private static (double minX, double minY, double maxX, double maxY) ComputeBoundingBox(IEnumerable<string> lines)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            double cx = 0, cy = 0;
            bool linearAbsoluteMode = true;

            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                ExtractCommentWithParentheses(raw,
                    out string codePart, out _, out bool commentOnly);
                if (commentOnly || string.IsNullOrWhiteSpace(codePart))
                    continue;

                string upper = codePart.ToUpperInvariant();
                if (G90Regex.IsMatch(upper) && !G901Regex.IsMatch(upper)) linearAbsoluteMode = true;
                if (G91Regex.IsMatch(upper) && !G911Regex.IsMatch(upper)) linearAbsoluteMode = false;

                var tokens = WordRegex.Matches(codePart)
                    .Cast<Match>()
                    .Select(m => (Letter: m.Groups[1].Value.ToUpperInvariant(), Val: m.Groups[2].Value))
                    .ToList();

                double? x = null, y = null;
                foreach (var t in tokens)
                {
                    if (!double.TryParse(t.Val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        continue;
                    if (t.Letter == "X") x = v;
                    else if (t.Letter == "Y") y = v;
                }
                bool has = x.HasValue || y.HasValue;
                if (!has) continue;

                if (linearAbsoluteMode)
                {
                    if (x.HasValue) cx = x.Value;
                    if (y.HasValue) cy = y.Value;
                }
                else
                {
                    cx += x ?? 0;
                    cy += y ?? 0;
                }

                if (cx < minX) minX = cx;
                if (cy < minY) minY = cy;
                if (cx > maxX) maxX = cx;
                if (cy > maxY) maxY = cy;
            }

            if (double.IsInfinity(minX))
            {
                minX = minY = maxX = maxY = 0;
            }
            return (minX, minY, maxX, maxY);
        }

        private static void ExtractCommentWithParentheses(
            string line,
            out string codePart,
            out string commentPart,
            out bool commentOnly)
        {
            codePart = string.Empty;
            commentPart = string.Empty;
            commentOnly = false;
            if (string.IsNullOrEmpty(line))
            {
                commentOnly = true;
                return;
            }

            string beforeSemicolon = line;
            string semicolonTail = "";
            int sc = line.IndexOf(';');
            if (sc >= 0)
            {
                beforeSemicolon = line.Substring(0, sc);
                semicolonTail = line.Substring(sc);
            }

            var codeSb = new StringBuilder();
            var commentSb = new StringBuilder();
            int i = 0;
            while (i < beforeSemicolon.Length)
            {
                char c = beforeSemicolon[i];
                if (c == '(')
                {
                    int start = i;
                    i++;
                    while (i < beforeSemicolon.Length && beforeSemicolon[i] != ')')
                        i++;
                    if (i < beforeSemicolon.Length && beforeSemicolon[i] == ')')
                        i++;
                    string paren = beforeSemicolon.Substring(start, i - start).Trim();
                    if (paren.Length > 0)
                    {
                        if (commentSb.Length > 0) commentSb.Append(' ');
                        commentSb.Append(paren);
                    }
                }
                else
                {
                    codeSb.Append(c);
                    i++;
                }
            }

            if (!string.IsNullOrEmpty(semicolonTail))
            {
                if (commentSb.Length > 0) commentSb.Append(' ');
                commentSb.Append(semicolonTail.Trim());
            }

            codePart = codeSb.ToString().Trim();
            commentPart = commentSb.ToString();
            if (string.IsNullOrEmpty(codePart))
                commentOnly = true;
        }

        private static string AppendComment(string code, string comment)
        {
            if (string.IsNullOrEmpty(comment)) return code;
            if (string.IsNullOrWhiteSpace(code)) return comment;
            return code + " " + comment;
        }

        private static double ParseOr(string txt)
        {
            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return 0;
        }

        private static string Format(double v, int decimals) =>
            v.ToString("0." + new string('#', Math.Max(0, decimals)), CultureInfo.InvariantCulture);

        private static void NormalizeAbsoluteXY(
             List<string> lines,
             List<(int index, double x, double y, bool hasX, bool hasY)> absCoords,
             int decimals)
        {
             if (absCoords.Count == 0) return;
             double minX = absCoords.Min(c => c.x);
             double minY = absCoords.Min(c => c.y);
             
             // Her zaman minimum değerleri 0'a çek (pozitif normalizasyon)
             double dx = -minX;  // minX'i 0'a getir
             double dy = -minY;  // minY'yi 0'a getir
             if (dx == 0 && dy == 0) return;

            // Tüm satırların yeni (X,Y) değerlerini global olarak uygula.
            // İstersek margin eklemek için dx += margin; dy += margin yapılabilir.
            ApplyGlobalTranslation(lines, absCoords, dx, dy, decimals);
         }
 
        private static void ApplyGlobalTranslation(
            List<string> lines,
            List<(int index, double x, double y, bool hasX, bool hasY)> absCoords,
            double dx,
            double dy,
            int decimals)
        {
            // Hızlı erişim için dictionary: lineIndex -> (newX,newY)
            var map = absCoords
                .GroupBy(c => c.index)
                .ToDictionary(g => g.Key,
                              g => (X: g.Last().x + dx, Y: g.Last().y + dy,
                                    hadX: g.Last().hasX, hadY: g.Last().hasY));

            foreach (var kv in map)
                lines[kv.Key] = RewriteLineWithAbsoluteXY(lines[kv.Key], kv.Value.X, kv.Value.Y, decimals, forceAddBoth:true);
        }

        private static string RewriteLineWithAbsoluteXY(string line, double absX, double absY, int decimals, bool forceAddBoth)
        {
            ExtractCommentWithParentheses(line, out string codePart, out string commentPart, out bool commentOnly);
            if (commentOnly || string.IsNullOrWhiteSpace(codePart))
                return line;

            var tokens = WordRegex.Matches(codePart)
                .Cast<Match>()
                .Select(m => (Letter: m.Groups[1].Value, ValueText: m.Groups[2].Value, Raw: m.Value))
                .ToList();

            bool hasX = tokens.Any(t => string.Equals(t.Letter, "X", StringComparison.OrdinalIgnoreCase));
            bool hasY = tokens.Any(t => string.Equals(t.Letter, "Y", StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                string L = t.Letter.ToUpperInvariant();
                if (L == "X")
                {
                    sb.Append("X").Append(Format(absX, decimals)).Append(' ');
                }
                else if (L == "Y")
                {
                    sb.Append("Y").Append(Format(absY, decimals)).Append(' ');
                }
                else
                {
                    sb.Append(t.Raw).Append(' ');
                }
            }

            if (forceAddBoth)
            {
                if (!hasX)
                    sb.Append("X").Append(Format(absX, decimals)).Append(' ');
                if (!hasY)
                    sb.Append("Y").Append(Format(absY, decimals)).Append(' ');
            }

            return AppendComment(sb.ToString().TrimEnd(), commentPart);
        }

        #endregion

        #region Test Methods

        public static void TestNegativeCoordinateFix()
        {
            var testLines = new[]
            {
                "G21",
                "G90", 
                "G00 X-60 Y-30",
                "G01 X-40 Y10 F400",
                "G01 X20 Y50 F400",
                "G00 X-60 Y-30"
            };

            App.MainController?.AddLogMessage("=== NEGATIVE COORDINATE FIX TEST ===");
            App.MainController?.AddLogMessage("Original G-Code (with negative coords):");
            foreach (var line in testLines)
                App.MainController?.AddLogMessage($"  {line}");

            var outcome = Rotate90CWFittedOutcome(testLines, 
                RotationPivotMode.Origin, decimals: 3);

            App.MainController?.AddLogMessage($"\nSource Bounds: ({outcome.SourceMinX:F1},{outcome.SourceMinY:F1}) to ({outcome.SourceMaxX:F1},{outcome.SourceMaxY:F1})");
            App.MainController?.AddLogMessage($"After Rotation+Fit: ({outcome.RotatedAfterFitMinX:F1},{outcome.RotatedAfterFitMinY:F1}) to ({outcome.RotatedAfterFitMaxX:F1},{outcome.RotatedAfterFitMaxY:F1})");
            App.MainController?.AddLogMessage($"Applied Shift: ({outcome.AppliedShiftX:F1},{outcome.AppliedShiftY:F1})");
            
            App.MainController?.AddLogMessage("\n✅ Resulting G-Code (should have NO negative coordinates):");
            foreach (var line in outcome.Lines)
                App.MainController?.AddLogMessage($"  {line}");
            
            bool hasNegatives = outcome.Lines.Any(line => 
                Regex.IsMatch(line, @"\b[XY]-\d", RegexOptions.IgnoreCase));
            
            App.MainController?.AddLogMessage($"\n🎯 TEST RESULT: {(hasNegatives ? "❌ FAILED - Still has negatives!" : "✅ PASSED - No negatives found")}");
            App.MainController?.AddLogMessage("=== TEST COMPLETE ===\n");
        }

        #endregion
    }

    internal static class RotationLogExporter
    {
        public static void WriteRotationLog(string filePath, IEnumerable<RotatedGCodeGenerator.RotationLogEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Rotation Debug Log");
            sb.AppendLine("# Line | Mode(Before->After) | IsArc | CommentOnly | Orig(X,Y,I,J) | UsedSrc(X,Y) | Rot(X,Y,I,J) | Warnings");
            foreach (var e in entries)
            {
                sb.Append(e.LineNumber).Append(" | ");
                sb.Append(e.LinearAbsoluteBefore ? "ABS" : "REL").Append("->")
                  .Append(e.LinearAbsoluteAfter ? "ABS" : "REL").Append(" | ");
                sb.Append(e.IsArc ? "ARC" : "LIN").Append(" | ");
                sb.Append(e.CommentOnly ? "CMT" : "-").Append(" | ");
                sb.AppendFormat(CultureInfo.InvariantCulture, "({0},{1},{2},{3}) | ",
                    F(e.OrigX), F(e.OrigY), F(e.OrigI), F(e.OrigJ));
                sb.AppendFormat(CultureInfo.InvariantCulture, "({0},{1}) | ",
                    F(e.SourceXUsed), F(e.SourceYUsed));
                sb.AppendFormat(CultureInfo.InvariantCulture, "({0},{1},{2},{3}) | ",
                    F(e.RotX), F(e.RotY), F(e.RotI), F(e.RotJ));
                if (e.Warnings.Count > 0) sb.Append(string.Join(";", e.Warnings));
                sb.AppendLine();
                sb.AppendLine("  ORG: " + e.Original);
                sb.AppendLine("  ROT: " + e.Rotated);
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            string F(double? v) => v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        }
    }
}