using System;
using GrblStreamer.Models;

namespace GrblStreamer.Events
{
    /// <summary>
    /// GRBL'den veri alındığında tetiklenir
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        public string RawData { get; private set; }
        public DateTime Timestamp { get; private set; }

        public DataReceivedEventArgs(string rawData)
        {
            RawData = rawData;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Durum güncellemesi alındığında tetiklenir
    /// </summary>
    public class StatusUpdateEventArgs : EventArgs
    {
        public GrblStatus Status { get; private set; }

        public StatusUpdateEventArgs(GrblStatus status)
        {
            Status = status;
        }
    }

    /// <summary>
    /// OK yanıtı alındığında tetiklenir
    /// </summary>
    public class OkReceivedEventArgs : EventArgs
    {
        public string Command { get; private set; }
        
        public OkReceivedEventArgs(string command)
        {
            Command = command;
        }
    }

    /// <summary>
    /// Hata alındığında tetiklenir
    /// </summary>
    public class ErrorReceivedEventArgs : EventArgs
    {
        public int ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public string Command { get; private set; }

        public ErrorReceivedEventArgs(int errorCode, string errorMessage, string command)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            Command = command;
        }
    }

    /// <summary>
    /// Alarm alındığında tetiklenir
    /// </summary>
    public class AlarmReceivedEventArgs : EventArgs
    {
        public int AlarmCode { get; private set; }
        public string AlarmMessage { get; private set; }

        public AlarmReceivedEventArgs(int alarmCode, string alarmMessage)
        {
            AlarmCode = alarmCode;
            AlarmMessage = alarmMessage;
        }
    }

    /// <summary>
    /// Streaming ilerleme güncellemesi
    /// </summary>
    public class StreamingProgressEventArgs : EventArgs
    {
        public StreamingStats Stats { get; private set; }

        public StreamingProgressEventArgs(StreamingStats stats)
        {
            Stats = stats;
        }
    }

    /// <summary>
    /// İş tamamlandığında tetiklenir
    /// </summary>
    public class JobCompletedEventArgs : EventArgs
    {
        public bool Success { get; private set; }
        public StreamingStats Stats { get; private set; }
        public string ErrorMessage { get; private set; }

        public JobCompletedEventArgs(bool success, StreamingStats stats, string errorMessage = null)
        {
            Success = success;
            Stats = stats;
            ErrorMessage = errorMessage;
        }
    }
}
