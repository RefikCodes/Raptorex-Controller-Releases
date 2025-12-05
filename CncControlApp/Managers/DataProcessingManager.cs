using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks; // added for TaskCompletionSource
using CncControlApp.Services;

namespace CncControlApp.Managers
{
    /// <summary>
    /// Data Processing Manager - Veri işleme, parsing ve koordinat güncellemeleri
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class DataProcessingManager : IDisposable
    {
        #region Fields

        private readonly ConcurrentQueue<string> _receivedDataQueue = new ConcurrentQueue<string>();
        private readonly StringBuilder _serialBuffer;
        private readonly object _bufferLock = new object();

        // Verbose periodic status summaries are disabled by default to keep console clean
        private const bool ENABLE_STATUS_SUMMARY_LOGS = false;

        // Buffer constants
        private const int MAX_BUFFER_SIZE = 8192;
        private const int PROCESSED_RESPONSES_LIMIT = 100;

        // Delegates
        private readonly Action<string> _addLogMessageDelegate;
        private readonly Action<string, string> _updateMachineStatusDelegate;
        private readonly Action<MachineStatus> _updateCoordinatesDelegate;

        // WCO tracking
        private double _lastWcoX = 0;
        private double _lastWcoY = 0;
        private double _lastWcoZ = 0;
        private double _lastWcoA = 0;

        // Status logging throttling
        private string _lastLoggedStatus = "";
        private DateTime _lastStatusLogTime = DateTime.MinValue;

        // Response processing
        private readonly ConcurrentDictionary<string, byte> _processedResponses = new ConcurrentDictionary<string, byte>();
        private volatile int _processedResponsesCount = 0;

        private string _lastMachineState = "";

        // ✅ Enhanced MPos debugging
        private int _statusReportCount = 0;
        private int _mposFoundCount = 0;
        private int _wposFoundCount = 0;
        private DateTime _lastMposDebugLog = DateTime.MinValue;
        
        // Persistent spindle/coolant state (maintained across status reports)
        private bool _lastSpindleState = false;
        private bool _lastCoolantState = false;

        public event Action<int, string> AlarmDetected; // NEW: alarm notification (code, rawLine)
    // NEW: Report executing line (from status report field 'Ln' or 'line')
    public event Action<int> ExecutingLineReported;
    
        // Event to notify spindle/coolant state changes (for LED updates)
        public event Action<bool, bool> AccessoryStateChanged;  // (isSpindleOn, isCoolantOn)

        #endregion

        #region Constructor

        /// <summary>
        /// DataProcessingManager constructor
        /// </summary>
        /// <param name="addLogMessage">Log mesajı ekleme delegate'i</param>
        /// <param name="updateMachineStatus">Makine durumu güncelleme delegate'i</param>
        /// <param name="updateCoordinates">Koordinat güncelleme delegate'i</param>
        public DataProcessingManager(
            Action<string> addLogMessage,
            Action<string, string> updateMachineStatus,
            Action<MachineStatus> updateCoordinates)
        {
            _addLogMessageDelegate = addLogMessage ?? throw new ArgumentNullException(nameof(addLogMessage));
            _updateMachineStatusDelegate = updateMachineStatus ?? throw new ArgumentNullException(nameof(updateMachineStatus));
            _updateCoordinatesDelegate = updateCoordinates ?? throw new ArgumentNullException(nameof(updateCoordinates));

            _serialBuffer = new StringBuilder(MAX_BUFFER_SIZE);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gelen veriyi kuyruğa ekle
        /// </summary>
        /// <param name="data">İşlenecek veri</param>
        public void EnqueueReceivedData(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                _receivedDataQueue.Enqueue(data);
                DiagnosticTimingService.Record("EnqueueReceivedData");
            }
        }

        /// <summary>
        /// Kuyrukta bekleyen verileri işle
        /// </summary>
        public void ProcessReceivedData()
        {
            DiagnosticTimingService.Record("ProcessReceivedData.Start");
            if (_receivedDataQueue.IsEmpty) { DiagnosticTimingService.Record("ProcessReceivedData.Empty"); return; }

            StringBuilder receivedBlock = new StringBuilder();
            while (_receivedDataQueue.TryDequeue(out string data))
            {
                receivedBlock.Append(data);
                DiagnosticTimingService.Record("DequeuedLine");
            }

            if (receivedBlock.Length > 0)
            {
                lock (_bufferLock)
                {
                    _serialBuffer.Append(receivedBlock.ToString());
                    DiagnosticTimingService.Record("BufferAppendDone");
                    ProcessBuffer();
                }
            }
            DiagnosticTimingService.Record("ProcessReceivedData.End");
        }

        /// <summary>
        /// Buffer'ı temizle
        /// </summary>
        public void ClearBuffer()
        {
            try
            {
                lock (_bufferLock)
                {
                    _serialBuffer.Clear();

                    // ✅ Reset capacity if it grew too large
                    if (_serialBuffer.Capacity > MAX_BUFFER_SIZE * 2)
                    {
                        _serialBuffer.Capacity = MAX_BUFFER_SIZE;
                    }
                }

                System.Diagnostics.Debug.WriteLine("Buffer cleared successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearBuffer error: {ex.Message}");
            }
        }

        /// <summary>
        /// Veri kuyruğunu temizle
        /// </summary>
        public void ClearDataQueue()
        {
            try
            {
                // Clear any pending commands in the received data queue
                while (_receivedDataQueue.TryDequeue(out _))
                {
                    // Clear queue
                }

                System.Diagnostics.Debug.WriteLine("Data queue cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearDataQueue error: {ex.Message}");
            }
        }

        /// <summary>
        /// Buffer durumunu al
        /// </summary>
        public void GetBufferStatus()
        {
            try
            {
                lock (_bufferLock)
                {
                    var bufferSize = _serialBuffer.Length;
                    var bufferCapacity = _serialBuffer.Capacity;
                    var queueCount = _receivedDataQueue.Count;

                    System.Diagnostics.Debug.WriteLine($"Data Processing Status - Buffer Size: {bufferSize}, Capacity: {bufferCapacity}, Queue: {queueCount}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetBufferStatus error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Serial buffer'ını işle
        /// </summary>
        private void ProcessBuffer()
        {
            DiagnosticTimingService.Record("ProcessBuffer.Start");
            if (_serialBuffer.Length > MAX_BUFFER_SIZE)
            {
                System.Diagnostics.Debug.WriteLine($"Buffer overflow detected: {_serialBuffer.Length} chars");
                string content = _serialBuffer.ToString();
                _serialBuffer.Clear();
                int keepFromIndex = content.Length / 2;
                if (keepFromIndex < content.Length)
                {
                    _serialBuffer.Append(content.Substring(keepFromIndex));
                }
                System.Diagnostics.Debug.WriteLine($"Buffer trimmed to: {_serialBuffer.Length} chars");
            }

            string bufferContent = _serialBuffer.ToString();
            DiagnosticTimingService.Record("ProcessBuffer.BufferToString");
            int startIndex, endIndex;

            // ✅ Status report parsing (<...> formatında)
            while ((startIndex = bufferContent.IndexOf('<')) != -1 &&
                   (endIndex = bufferContent.IndexOf('>')) != -1 &&
                   endIndex > startIndex)
            {
                string msg = bufferContent.Substring(startIndex, endIndex - startIndex + 1);
                DiagnosticTimingService.Record("FoundStatusReport");
                ParseAndUpdateCoordinates(msg);
                bufferContent = bufferContent.Remove(startIndex, msg.Length);
            }

            // ✅ Line-based response parsing
            int processedLines = 0;
            int lineEndIndex;
            while ((lineEndIndex = bufferContent.IndexOf('\n')) != -1 && processedLines < 50)
            {
                string line = bufferContent.Substring(0, lineEndIndex).Trim();
                if (!string.IsNullOrWhiteSpace(line) && !line.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticTimingService.Record("ProcessBuffer.LineFound");
                    // NEW: Alarm detection (GRBL/FluidNC) e.g. ALARM:14 or info message containing Unhomed
                    if (line.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
                    {
                        int code = -1;
                        var codePart = line.Substring(6).Trim();
                        int.TryParse(codePart, out code);
                        try { AlarmDetected?.Invoke(code, line); } catch { }
                    }
                    else if (line.IndexOf("Unhomed", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("ALARM", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Treat as generic unhomed alarm (code14 commonly Line length exceeded but message says Unhomed on some firmwares)
                        try { AlarmDetected?.Invoke(14, line); } catch { }
                    }
                    
                    // Parse $G response for spindle/coolant modal state
                    // Format: [GC:G0 G54 G17 G21 G90 G94 M3 S10000 M9 F500 T0]
                    if (line.StartsWith("[GC:", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseGCModalState(line);
                    }

                    if (IsImportantMessageAdvanced(line))
                    {
                        bool isNewMessage = ShouldLogMessage(line);
                        if (isNewMessage)
                        {
                            var logMessage = $"> {line}";
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _addLogMessageDelegate(logMessage);
                            }), DispatcherPriority.Background);
                        }
                    }
                }
                bufferContent = bufferContent.Remove(0, lineEndIndex + 1);
                processedLines++;
            }

            _serialBuffer.Clear();
            if (!string.IsNullOrEmpty(bufferContent))
            {
                _serialBuffer.Append(bufferContent);
            }
            DiagnosticTimingService.Record("ProcessBuffer.End");
        }

        /// <summary>
        /// Koordinat verilerini parse et ve güncelle
        /// </summary>
        /// <param name="statusReport">Status report string'i</param>
        private void ParseAndUpdateCoordinates(string statusReport)
        {
            try
            {
                DiagnosticTimingService.Record("ParseAndUpdateCoords.Start");
                var machineStatus = new MachineStatus();
                bool hasWPos = false;
                bool hasWCO = false;
                bool hasMPos = false;

                // ✅ Increment status report counter for diagnostics
                _statusReportCount++;

                // ✅ Enhanced raw status logging - always log for first50 reports or every100th report
                bool shouldLogRawStatus = _statusReportCount <= 50 || _statusReportCount % 100 == 0;
                if (shouldLogRawStatus)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Raw status: {statusReport}");
                }
                
                // Log first 5 status reports to UI for debugging accessory state
                if (_statusReportCount <= 5)
                {
                    try { _addLogMessageDelegate?.Invoke($"> 📡 Status #{_statusReportCount}: {statusReport}"); } catch { }
                    ErrorLogger.LogInfo($"[STATUS_LOG #{_statusReportCount}] {statusReport}");
                }

                //1) Parse status header
                var statusMatch = Regex.Match(statusReport, @"<([^|>]+)");
                if (statusMatch.Success)
                {
                    // Sadece kısa state token ("Idle", "Run", "Jog", "Hold", "Alarm", ... )
                    string newState = statusMatch.Groups[1].Value.Trim();

                    if (!string.Equals(_lastMachineState, newState, StringComparison.Ordinal))
                    {
                        _lastMachineState = newState;
                        // Üst panel sadece kısa metni görsün
                        _updateMachineStatusDelegate(newState, null);
                        DiagnosticTimingService.Record("MachineStatus.Updated");
                        if (shouldLogRawStatus)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] State changed to: {newState}");
                        }
                    }
                }

                //2. Parse MPos first (should always be present in GRBL)
                var mPosMatch = Regex.Match(statusReport, @"MPos:([\d\.-]+),([\d\.-]+),([\d\.-]+)(?:,([\d\.-]+))?", RegexOptions.IgnoreCase);
                if (mPosMatch.Success)
                {
                    DiagnosticTimingService.Record("MPos.Parsed");
                    hasMPos = true;

                    double.TryParse(mPosMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mX);
                    double.TryParse(mPosMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mY);
                    double.TryParse(mPosMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mZ);

                    machineStatus.X = mX;
                    machineStatus.Y = mY;
                    machineStatus.Z = mZ;

                    if (shouldLogRawStatus)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ✅ MPos FOUND and parsed: X={mX:F3}, Y={mY:F3}, Z={mZ:F3}");
                    }

                    if (mPosMatch.Groups[4].Success && double.TryParse(mPosMatch.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mA))
                    {
                        machineStatus.A = mA;
                        if (shouldLogRawStatus)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] MPos A: A={mA:F3}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ❌ NO MPos FOUND in status report: {statusReport}");
                    if (_statusReportCount <= 10 || _statusReportCount % 50 == 0)
                    {
                        _addLogMessageDelegate($"> ⚠️ MPos missing in status #{_statusReportCount}: {statusReport.Substring(0, Math.Min(50, statusReport.Length))}...");
                    }
                }

                // NEW: Parse FS: feed,spindle and keep feed for UI (CurrentFeed)
                // Also handle F: format (GRBL sometimes sends just F: without spindle)
                var fsMatch = Regex.Match(statusReport, @"\|FS:([\d\.-]+),([\d\.-]+)");
                if (fsMatch.Success)
                {
                    if (double.TryParse(fsMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double feedVal))
                    {
                        machineStatus.CurrentFeed = feedVal;
                    }
                }
                else
                {
                    // Try F: only format (without spindle)
                    var fOnlyMatch = Regex.Match(statusReport, @"\|F:([\d\.-]+)");
                    if (fOnlyMatch.Success)
                    {
                        if (double.TryParse(fOnlyMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double feedVal))
                        {
                            machineStatus.CurrentFeed = feedVal;
                        }
                    }
                }

                // Parse accessory state (A: field) for spindle and coolant status
                // GRBL format: |A:SFM where S=Spindle CW, C=Spindle CCW, F=Flood coolant, M=Mist coolant
                // NOTE: GRBL only sends A: field when accessories are active
                // We use persistent state to avoid LED flickering when A: field is intermittent
                try
                {
                    var aMatch = Regex.Match(statusReport, @"\|A:([SCFM]+)", RegexOptions.IgnoreCase);
                    if (aMatch.Success)
                    {
                        string accessories = aMatch.Groups[1].Value.ToUpper();
                        bool spindleOn = accessories.Contains("S") || accessories.Contains("C");
                        bool coolantOn = accessories.Contains("F") || accessories.Contains("M");
                        
                        // Update persistent state and log when state changes
                        if (_lastSpindleState != spindleOn || _lastCoolantState != coolantOn)
                        {
                            string msg = $"> 🔧 Accessory state: A:{accessories} → Spindle={spindleOn}, Coolant={coolantOn}";
                            _addLogMessageDelegate(msg);
                            ErrorLogger.LogInfo($"[ACCESSORY] {msg}");
                            _lastSpindleState = spindleOn;
                            _lastCoolantState = coolantOn;
                        }
                    }
                    // NOTE: If A: field is missing, we do NOT reset state!
                    // GRBL doesn't always include A: field in every status report.
                    // Only M5/M9 commands should turn off spindle/coolant.
                    
                    // Always use persistent state for machineStatus
                    machineStatus.IsSpindleOn = _lastSpindleState;
                    machineStatus.IsCoolantOn = _lastCoolantState;
                }
                catch { }

                // NEW: Parse executing line index (FluidNC/GrblHAL style) e.g. |Ln:123 or |line:123
                try
                {
                    var lnMatch = Regex.Match(statusReport, @"\|(Ln|line):\s*(\d+)", RegexOptions.IgnoreCase);
                    if (lnMatch.Success && int.TryParse(lnMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int oneBasedLine))
                    {
                        try { ExecutingLineReported?.Invoke(oneBasedLine); } catch { }
                        if (_lastLoggedStatus != "__LINE_FIELD_DETECTED__")
                        {
                            _addLogMessageDelegate("> ✅ Status report 'line' alanı tespit edildi – gerçek yürütülen satır izlenecek");
                            _lastLoggedStatus = "__LINE_FIELD_DETECTED__";
                        }
                    }
                }
                catch { }

                //3. Check for WPos (direct work position)
                var wPosMatch = Regex.Match(statusReport, @"WPos:([\d\.-]+),([\d\.-]+),([\d\.-]+)(?:,([\d\.-]+))?", RegexOptions.IgnoreCase);
                if (wPosMatch.Success)
                {
                    hasWPos = true;
                    _wposFoundCount++;

                    double.TryParse(wPosMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wX);
                    double.TryParse(wPosMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wY);
                    double.TryParse(wPosMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wZ);

                    machineStatus.WorkX = wX;
                    machineStatus.WorkY = wY;
                    machineStatus.WorkZ = wZ;

                    if (shouldLogRawStatus)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WPos FOUND: X={wX:F3}, Y={wY:F3}, Z={wZ:F3}");
                    }

                    if (wPosMatch.Groups[4].Success && double.TryParse(wPosMatch.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wA))
                    {
                        machineStatus.WorkA = wA;
                        if (shouldLogRawStatus)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WPos A: A={wA:F3}");
                        }
                    }
                }

                //4. Check for WCO and update if present
                var wcoMatch = Regex.Match(statusReport, @"WCO:([\d\.-]+),([\d\.-]+),([\d\.-]+)(?:,([\d\.-]+))?", RegexOptions.IgnoreCase);
                if (wcoMatch.Success)
                {
                    hasWCO = true;
                    double.TryParse(wcoMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wcoX);
                    double.TryParse(wcoMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wcoY);
                    double.TryParse(wcoMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wcoZ);

                    _lastWcoX = wcoX;
                    _lastWcoY = wcoY;
                    _lastWcoZ = wcoZ;

                    if (shouldLogRawStatus)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WCO updated: X={wcoX:F3}, Y={wcoY:F3}, Z={wcoZ:F3}");
                    }

                    if (wcoMatch.Groups[4].Success && double.TryParse(wcoMatch.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double wcoA))
                    {
                        _lastWcoA = wcoA;
                        if (shouldLogRawStatus)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WCO A: A={wcoA:F3}");
                        }
                    }
                }

                //5. Handle missing MPos case - calculate from WPos + WCO if available
                if (!hasMPos && hasWPos)
                {
                    machineStatus.X = machineStatus.WorkX + _lastWcoX;
                    machineStatus.Y = machineStatus.WorkY + _lastWcoY;
                    machineStatus.Z = machineStatus.WorkZ + _lastWcoZ;
                    machineStatus.A = machineStatus.WorkA + _lastWcoA;

                    if (shouldLogRawStatus)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ⚠️ Calculated MPos from WPos+WCO:");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] MX = {machineStatus.WorkX:F3} + {_lastWcoX:F3} = {machineStatus.X:F3}");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] MY = {machineStatus.WorkY:F3} + {_lastWcoY:F3} = {machineStatus.Y:F3}");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] MZ = {machineStatus.WorkZ:F3} + {_lastWcoZ:F3} = {machineStatus.Z:F3}");
                    }
                }

                //6. Calculate work coordinates if no direct WPos but we have MPos
                if (!hasWPos && hasMPos)
                {
                    machineStatus.WorkX = machineStatus.X - _lastWcoX;
                    machineStatus.WorkY = machineStatus.Y - _lastWcoY;
                    machineStatus.WorkZ = machineStatus.Z - _lastWcoZ;
                    machineStatus.WorkA = machineStatus.A - _lastWcoA;

                    if (shouldLogRawStatus)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Work calculated from MPos-WCO:");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WorkX = {machineStatus.X:F3} - {_lastWcoX:F3} = {machineStatus.WorkX:F3}");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WorkY = {machineStatus.Y:F3} - {_lastWcoY:F3} = {machineStatus.WorkY:F3}");
                        System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] WorkZ = {machineStatus.Z:F3} - {_lastWcoZ:F3} = {machineStatus.WorkZ:F3}");
                    }
                }

                if (shouldLogRawStatus)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Final MachineStatus object:");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Machine: X={machineStatus.X:F3}, Y={machineStatus.Y:F3}, Z={machineStatus.Z:F3}, A={machineStatus.A:F3}");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Work: X={machineStatus.WorkX:F3}, Y={machineStatus.WorkY:F3}, Z={machineStatus.WorkZ:F3}, A={machineStatus.WorkA:F3}");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] Flags: hasMPos={hasMPos}, hasWPos={hasWPos}, hasWCO={hasWCO}");
                }

                if (ENABLE_STATUS_SUMMARY_LOGS && _statusReportCount > 0 && _statusReportCount % 100 == 0)
                {
                    double mposPercent = (double)_mposFoundCount / _statusReportCount * 100;
                    double wposPercent = (double)_wposFoundCount / _statusReportCount * 100;

                    _addLogMessageDelegate($"> 📊 Status Report Summary (after {_statusReportCount} reports):");
                    _addLogMessageDelegate($"> MPos found: {_mposFoundCount}/{_statusReportCount} ({mposPercent:F1}%)");
                    _addLogMessageDelegate($"> WPos found: {_wposFoundCount}/{_statusReportCount} ({wposPercent:F1}%)");

                    if (_mposFoundCount == 0)
                    {
                        _addLogMessageDelegate($"> ⚠️ ISSUE: No MPos data received from machine!");
                        _addLogMessageDelegate($"> 💡 This indicates the GRBL machine may be configured to report WPos only");
                        _addLogMessageDelegate($"> 💡 Try sending '$10=0' to enable MPos reporting, or '$10=1' for WPos only");
                    }
                }

                _updateCoordinatesDelegate(machineStatus);
                DiagnosticTimingService.Record("Coordinates.Updated");

                if (shouldLogRawStatus)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ✅ Update delegate called successfully");
                }
                DiagnosticTimingService.Record("ParseAndUpdateCoords.End");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ❌ ParseAndUpdateCoordinates error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG #{_statusReportCount}] ❌ Stack trace: {ex.StackTrace}");

                if (_statusReportCount % 50 == 0)
                {
                    _addLogMessageDelegate($"> ❌ Coordinate parsing error #{_statusReportCount}: {ex.Message}");
                }
            }
        }

        private bool ShouldLogMessage(string line)
        {
            try
            {
                if (IsAlwaysLogMessage(line))
                {
                    return true;
                }

                bool isNewMessage = _processedResponses.TryAdd(line, 0);

                if (isNewMessage)
                {
                    int currentCount = Interlocked.Increment(ref _processedResponsesCount);

                    if (currentCount > PROCESSED_RESPONSES_LIMIT)
                    {
                        _processedResponses.Clear();
                        Interlocked.Exchange(ref _processedResponsesCount, 0);
                    }
                }

                return isNewMessage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShouldLogMessage error: {ex.Message}");
                return true;
            }
        }

        private bool IsImportantMessageAdvanced(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            try
            {
                if (message.StartsWith("<") && message.EndsWith(">")) return false;
                if (message.Contains("Bf:") || message.Contains("FS:")) return false; // FS satır içi status'ta var, ayrı loglama
                if (message.Contains(",") && (message.Contains("-") || char.IsDigit(message[0]))) return false;
                if (message.Contains("|MPos:") || message.Contains("|WPos:") || message.Contains("|WCO:")) return false;
                if (IsNumericDataPattern(message)) return false;
                if (message.Equals("ok", StringComparison.OrdinalIgnoreCase)) return false;
                if (message.StartsWith("$") && char.IsDigit(message.ElementAtOrDefault(1))) return false;

                string lowerMessage = message.ToLower();
                return lowerMessage.Contains("error") ||
                       lowerMessage.Contains("alarm") ||
                       lowerMessage.Contains("hold") ||
                       lowerMessage.Contains("door") ||
                       lowerMessage.Contains("probe") ||
                       lowerMessage.Contains("grbl") ||
                       lowerMessage.Contains("unlock") ||
                       lowerMessage.Contains("reset") ||
                       lowerMessage.Contains("home");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsNumericDataPattern(string message)
        {
            try
            {
                if (message.Contains("|") && message.Contains(":"))
                {
                    var parts = message.Split('|');
                    if (parts.Length >= 2)
                    {
                        if (int.TryParse(parts[0], out _))
                        {
                            var secondPart = parts[1];
                            if (secondPart.Contains(":"))
                            {
                                var colonParts = secondPart.Split(':');
                                if (colonParts.Length == 2)
                                {
                                    var afterColon = colonParts[1];
                                    return afterColon.All(c => char.IsDigit(c) || c == ',' || c == '.' || c == '-');
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private bool IsAlwaysLogMessage(string message)
        {
            string lowerMessage = message.ToLower();
            return lowerMessage.Contains("error") ||
                   lowerMessage.Contains("alarm") ||
                   lowerMessage.Contains("emergency") ||
                   lowerMessage.Contains("reset");
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                if (_statusReportCount > 0)
                {
                    double mposPercent = (double)_mposFoundCount / _statusReportCount * 100;
                    double wposPercent = (double)_wposFoundCount / _statusReportCount * 100;

                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG] FINAL REPORT:");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG] Total Status Reports: {_statusReportCount}");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG] MPos Found: {_mposFoundCount} ({mposPercent:F1}%)");
                    System.Diagnostics.Debug.WriteLine($"[MPOS_DEBUG] WPos Found: {_wposFoundCount} ({wposPercent:F1}%)");
                }

                ClearBuffer();
                ClearDataQueue();
                _processedResponses?.Clear();

                System.Diagnostics.Debug.WriteLine("DataProcessingManager disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DataProcessingManager dispose error: {ex.Message}");
            }
        }

        #endregion
        
        #region Modal State Parsing
        
        /// <summary>
        /// Parse $G response to extract spindle and coolant modal state
        /// Format: [GC:G0 G54 G17 G21 G90 G94 M3 S10000 M9 F500 T0]
        /// M3 = Spindle CW, M4 = Spindle CCW, M5 = Spindle OFF
        /// M7 = Mist coolant, M8 = Flood coolant, M9 = Coolant OFF
        /// </summary>
        private void ParseGCModalState(string gcLine)
        {
            try
            {
                // Extract content between [GC: and ]
                int startIdx = gcLine.IndexOf("[GC:", StringComparison.OrdinalIgnoreCase);
                int endIdx = gcLine.LastIndexOf(']');
                if (startIdx < 0 || endIdx <= startIdx) return;
                
                string content = gcLine.Substring(startIdx + 4, endIdx - startIdx - 4).Trim();
                var tokens = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                bool spindleOn = false;
                bool coolantOn = false;
                
                foreach (var token in tokens)
                {
                    string upper = token.ToUpperInvariant();
                    
                    // Spindle modes
                    if (upper == "M3" || upper == "M4")
                    {
                        spindleOn = true;
                    }
                    else if (upper == "M5")
                    {
                        spindleOn = false;
                    }
                    // Coolant modes
                    else if (upper == "M7" || upper == "M8")
                    {
                        coolantOn = true;
                    }
                    else if (upper == "M9")
                    {
                        coolantOn = false;
                    }
                }
                
                // Always update state (not just on change) to ensure LED sync
                _lastSpindleState = spindleOn;
                _lastCoolantState = coolantOn;
                
                // Always notify listeners for LED updates
                try { AccessoryStateChanged?.Invoke(spindleOn, coolantOn); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseGCModalState error: {ex.Message}");
            }
        }
        
        #endregion
    }

    // Add this static cache inside namespace CncControlApp.Managers (top or bottom of file)
    public static class ProbeContactCache
    {
        private static readonly object _lock = new object();
        private static DateTime _tsUtc;
        private static double _rawX, _rawY, _rawZ;

        public static event Action<double, double, double, DateTime> Contact; // (X,Y,Z,TsUtc)

        private static readonly ConcurrentQueue<TaskCompletionSource<(double x, double y, double z, DateTime ts)>> _waiters = new ConcurrentQueue<TaskCompletionSource<(double, double, double, DateTime)>>();

        public static void SetRaw(double x, double y, double z)
        {
            TaskCompletionSource<(double, double, double, DateTime ts)> waiter = null;
            DateTime ts;
            lock (_lock)
            {
                _rawX = x; _rawY = y; _rawZ = z;
                _tsUtc = DateTime.UtcNow;
                ts = _tsUtc;
                if (_waiters.TryDequeue(out waiter))
                {
                }
            }
            try
            {
                Contact?.Invoke(x, y, z, ts);
            }
            catch { }

            if (waiter != null)
            {
                waiter.TrySetResult((x, y, z, ts));
            }
        }

        public static bool TryGetAfter(DateTime utcThreshold, out double x, out double y, out double z, out DateTime tsUtc)
        {
            lock (_lock)
            {
                if (_tsUtc > utcThreshold)
                {
                    x = _rawX; y = _rawY; z = _rawZ; tsUtc = _tsUtc;
                    return true;
                }
            }
            x = y = z = 0;
            tsUtc = DateTime.MinValue;
            return false;
        }

        public static Task<(double x, double y, double z, DateTime ts)> WaitNextAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<(double, double, double, DateTime ts)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);
            var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetCanceled();
                }
            });
            return tcs.Task;
        }
    }
}