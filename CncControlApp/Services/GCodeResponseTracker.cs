using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CncControlApp.Services
{
    /// <summary>
    /// Event-driven G-code response tracker - tracks sent commands and matches them with ok/error responses.
    /// Based on OpenBuilds-CONTROL buffer tracking approach.
    /// </summary>
    public class GCodeResponseTracker
    {
        #region Constants

        /// <summary>
        /// GRBL RX buffer size (127 bytes usable, 128 total - 1 for safety)
        /// </summary>
        public const int GRBL_RX_BUFFER_SIZE = 127;

        /// <summary>
        /// grblHAL has larger buffer
        /// </summary>
        public const int GRBLHAL_RX_BUFFER_SIZE = 1023;

        #endregion

        #region Private Fields

        private readonly object _lock = new object();
        
        /// <summary>
        /// Commands sent to GRBL that are waiting for ok/error response.
        /// Each entry is the command string (including newline byte count).
        /// </summary>
        private readonly Queue<SentCommand> _sentBuffer = new Queue<SentCommand>();

        /// <summary>
        /// Pending TaskCompletionSources waiting for ok response
        /// </summary>
        private readonly ConcurrentDictionary<int, TaskCompletionSource<CommandResult>> _pendingCommands 
            = new ConcurrentDictionary<int, TaskCompletionSource<CommandResult>>();

        private int _commandIdCounter = 0;
        private int _rxBufferSize = GRBL_RX_BUFFER_SIZE;
        private bool _blocked = false;

        // Last probe result for PRB response parsing
        private ProbeResponse _lastProbeResult;

        #endregion

        #region Events

        /// <summary>
        /// Fired when GRBL sends "ok" response
        /// </summary>
        public event EventHandler<TrackerOkEventArgs> OkReceived;

        /// <summary>
        /// Fired when GRBL sends "error:N" response
        /// </summary>
        public event EventHandler<TrackerErrorEventArgs> ErrorReceived;

        /// <summary>
        /// Fired when GRBL sends "ALARM:N" response
        /// </summary>
        public event EventHandler<TrackerAlarmEventArgs> AlarmReceived;

        /// <summary>
        /// Fired when GRBL sends "[PRB:x,y,z:1]" probe result
        /// </summary>
        public event EventHandler<TrackerProbeEventArgs> ProbeResultReceived;

        /// <summary>
        /// Fired when buffer becomes available after being blocked
        /// </summary>
        public event EventHandler BufferAvailable;

        #endregion

        #region Properties

        /// <summary>
        /// Number of commands in flight (sent but not yet acknowledged)
        /// </summary>
        public int InFlightCount
        {
            get { lock (_lock) { return _sentBuffer.Count; } }
        }

        /// <summary>
        /// Total bytes currently in GRBL's RX buffer
        /// </summary>
        public int BufferUsed
        {
            get
            {
                lock (_lock)
                {
                    return _sentBuffer.Sum(c => c.ByteCount);
                }
            }
        }

        /// <summary>
        /// Available space in GRBL's RX buffer
        /// </summary>
        public int BufferSpace => _rxBufferSize - BufferUsed;

        /// <summary>
        /// Whether sending is blocked due to full buffer
        /// </summary>
        public bool IsBlocked
        {
            get { lock (_lock) { return _blocked; } }
            private set { lock (_lock) { _blocked = value; } }
        }

        /// <summary>
        /// Last received probe result
        /// </summary>
        public ProbeResponse LastProbeResult
        {
            get { lock (_lock) { return _lastProbeResult; } }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Configure buffer size (call after detecting grblHAL)
        /// </summary>
        public void SetBufferSize(int size)
        {
            _rxBufferSize = Math.Max(64, size);
        }

        /// <summary>
        /// Check if a command can be sent (enough buffer space)
        /// </summary>
        public bool CanSend(string command)
        {
            int byteCount = GetByteCount(command);
            return byteCount <= BufferSpace;
        }

        /// <summary>
        /// Track a command that was sent to GRBL.
        /// Call this AFTER successfully writing to serial port.
        /// </summary>
        /// <param name="command">The G-code command (without newline)</param>
        /// <returns>Command ID for tracking, or -1 if buffer was full</returns>
        public int TrackSentCommand(string command)
        {
            int byteCount = GetByteCount(command);

            lock (_lock)
            {
                // Check buffer space
                if (byteCount > BufferSpace)
                {
                    _blocked = true;
                    return -1;
                }

                int id = Interlocked.Increment(ref _commandIdCounter);
                var sentCmd = new SentCommand
                {
                    Id = id,
                    Command = command,
                    ByteCount = byteCount,
                    SentTime = DateTime.UtcNow
                };

                _sentBuffer.Enqueue(sentCmd);
                return id;
            }
        }

        /// <summary>
        /// Track a command and return a Task that completes when ok/error is received.
        /// </summary>
        public Task<CommandResult> TrackCommandWithCompletionAsync(string command, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Register cancellation
            ct.Register(() => tcs.TrySetCanceled());

            int byteCount = GetByteCount(command);

            lock (_lock)
            {
                // Check buffer space
                if (byteCount > BufferSpace)
                {
                    _blocked = true;
                    tcs.SetResult(CommandResult.Failed("Buffer full", command));
                    return tcs.Task;
                }

                int id = Interlocked.Increment(ref _commandIdCounter);
                var sentCmd = new SentCommand
                {
                    Id = id,
                    Command = command,
                    ByteCount = byteCount,
                    SentTime = DateTime.UtcNow
                };

                _sentBuffer.Enqueue(sentCmd);
                _pendingCommands[id] = tcs;
            }

            return tcs.Task;
        }

        /// <summary>
        /// Call when "ok" is received from GRBL.
        /// Removes oldest command from buffer and signals completion.
        /// </summary>
        public void OnOkReceived()
        {
            SentCommand completed = null;
            TaskCompletionSource<CommandResult> tcs = null;

            lock (_lock)
            {
                if (_sentBuffer.Count > 0)
                {
                    completed = _sentBuffer.Dequeue();
                    _pendingCommands.TryRemove(completed.Id, out tcs);
                }

                // Check if we can unblock
                if (_blocked && BufferSpace > 0)
                {
                    _blocked = false;
                }
            }

            // Signal completion
            if (completed != null)
            {
                tcs?.TrySetResult(CommandResult.Success(completed));
                OkReceived?.Invoke(this, new TrackerOkEventArgs(completed));
            }

            // Notify buffer available
            if (!IsBlocked)
            {
                BufferAvailable?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Call when "error:N" is received from GRBL.
        /// Removes oldest command from buffer and signals error.
        /// </summary>
        public void OnErrorReceived(int errorCode, string errorMessage)
        {
            SentCommand failed = null;
            TaskCompletionSource<CommandResult> tcs = null;

            lock (_lock)
            {
                if (_sentBuffer.Count > 0)
                {
                    failed = _sentBuffer.Dequeue();
                    _pendingCommands.TryRemove(failed.Id, out tcs);
                }

                // Unblock on error
                if (_blocked)
                {
                    _blocked = false;
                }
            }

            // Signal error
            if (failed != null)
            {
                var result = CommandResult.Error(errorCode, errorMessage, failed);
                tcs?.TrySetResult(result);
                ErrorReceived?.Invoke(this, new TrackerErrorEventArgs(failed, errorCode, errorMessage));
            }
        }

        /// <summary>
        /// Call when "ALARM:N" is received from GRBL.
        /// Clears all pending commands.
        /// </summary>
        public void OnAlarmReceived(int alarmCode, string alarmMessage)
        {
            List<TaskCompletionSource<CommandResult>> pendingTasks;

            lock (_lock)
            {
                // Clear buffer - ALARM invalidates all pending commands
                pendingTasks = _pendingCommands.Values.ToList();
                _sentBuffer.Clear();
                _pendingCommands.Clear();
                _blocked = false;
            }

            // Cancel all pending tasks
            foreach (var tcs in pendingTasks)
            {
                tcs.TrySetResult(CommandResult.Alarm(alarmCode, alarmMessage));
            }

            AlarmReceived?.Invoke(this, new TrackerAlarmEventArgs(alarmCode, alarmMessage));
        }

        /// <summary>
        /// Call when "[PRB:x,y,z:1]" probe result is received.
        /// </summary>
        public void OnProbeResultReceived(double x, double y, double z, bool success)
        {
            var result = new ProbeResponse
            {
                X = x,
                Y = y,
                Z = z,
                Success = success,
                ReceivedTime = DateTime.UtcNow
            };

            lock (_lock)
            {
                _lastProbeResult = result;
            }

            ProbeResultReceived?.Invoke(this, new TrackerProbeEventArgs(result));
        }

        /// <summary>
        /// Parse PRB response string: "[PRB:1.234,5.678,-2.345:1]"
        /// </summary>
        public static bool TryParsePrbResponse(string data, out double x, out double y, out double z, out bool success)
        {
            x = y = z = 0;
            success = false;

            if (string.IsNullOrEmpty(data) || !data.StartsWith("[PRB:"))
                return false;

            try
            {
                // [PRB:1.234,5.678,-2.345:1]
                int start = 5; // After "[PRB:"
                int end = data.LastIndexOf(']');
                if (end < start) return false;

                string content = data.Substring(start, end - start);
                // content = "1.234,5.678,-2.345:1"

                string[] parts = content.Split(':');
                if (parts.Length != 2) return false;

                string[] coords = parts[0].Split(',');
                if (coords.Length < 3) return false;

                x = double.Parse(coords[0], CultureInfo.InvariantCulture);
                y = double.Parse(coords[1], CultureInfo.InvariantCulture);
                z = double.Parse(coords[2], CultureInfo.InvariantCulture);
                success = parts[1] == "1";

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reset tracker state (call on disconnect or soft reset)
        /// </summary>
        public void Reset()
        {
            List<TaskCompletionSource<CommandResult>> pendingTasks;

            lock (_lock)
            {
                pendingTasks = _pendingCommands.Values.ToList();
                _sentBuffer.Clear();
                _pendingCommands.Clear();
                _blocked = false;
                _lastProbeResult = null;
            }

            // Cancel all pending
            foreach (var tcs in pendingTasks)
            {
                tcs.TrySetCanceled();
            }
        }

        #endregion

        #region Private Methods

        private static int GetByteCount(string command)
        {
            // Command length + 1 for newline character
            return System.Text.Encoding.ASCII.GetByteCount(command) + 1;
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Represents a command sent to GRBL waiting for response
    /// </summary>
    public class SentCommand
    {
        public int Id { get; set; }
        public string Command { get; set; }
        public int ByteCount { get; set; }
        public DateTime SentTime { get; set; }

        public override string ToString() => $"[{Id}] {Command} ({ByteCount}b)";
    }

    /// <summary>
    /// Result of a command execution
    /// </summary>
    public class CommandResult
    {
        public bool IsSuccess { get; private set; }
        public bool IsError { get; private set; }
        public bool IsAlarm { get; private set; }
        public int ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public SentCommand Command { get; private set; }

        public static CommandResult Success(SentCommand cmd) => new CommandResult
        {
            IsSuccess = true,
            Command = cmd
        };

        public static CommandResult Error(int code, string message, SentCommand cmd) => new CommandResult
        {
            IsError = true,
            ErrorCode = code,
            ErrorMessage = message,
            Command = cmd
        };

        public static CommandResult Alarm(int code, string message) => new CommandResult
        {
            IsAlarm = true,
            ErrorCode = code,
            ErrorMessage = message
        };

        public static CommandResult Failed(string reason, string command) => new CommandResult
        {
            IsError = true,
            ErrorMessage = reason,
            Command = new SentCommand { Command = command }
        };
    }

    /// <summary>
    /// Probe result from GRBL
    /// </summary>
    public class ProbeResponse
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool Success { get; set; }
        public DateTime ReceivedTime { get; set; }
    }

    #endregion

    #region Event Args

    public class TrackerOkEventArgs : EventArgs
    {
        public SentCommand Command { get; }
        public TrackerOkEventArgs(SentCommand cmd) { Command = cmd; }
    }

    public class TrackerErrorEventArgs : EventArgs
    {
        public SentCommand Command { get; }
        public int ErrorCode { get; }
        public string ErrorMessage { get; }
        public TrackerErrorEventArgs(SentCommand cmd, int code, string msg)
        {
            Command = cmd;
            ErrorCode = code;
            ErrorMessage = msg;
        }
    }

    public class TrackerAlarmEventArgs : EventArgs
    {
        public int AlarmCode { get; }
        public string AlarmMessage { get; }
        public TrackerAlarmEventArgs(int code, string msg)
        {
            AlarmCode = code;
            AlarmMessage = msg;
        }
    }

    public class TrackerProbeEventArgs : EventArgs
    {
        public ProbeResponse Result { get; }
        public TrackerProbeEventArgs(ProbeResponse result) { Result = result; }
    }

    #endregion
}
