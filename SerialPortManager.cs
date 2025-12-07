// SerialPortManager.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace CncControlApp
{
    public class SerialPortManager
    {
        public class ConnectionDetails
        {
            public string PortName { get; set; }
            public int BaudRate { get; set; }
            public Parity Parity { get; set; }
            public int DataBits { get; set; }
            public StopBits StopBits { get; set; }
            public Handshake Handshake { get; set; }
            public bool DtrEnable { get; set; }
            public bool RtsEnable { get; set; }
            public int ReadTimeout { get; set; }
            public int WriteTimeout { get; set; }
            public int ReadBufferSize { get; set; }
            public int WriteBufferSize { get; set; }
            public string EncodingName { get; set; }
            public bool IsOpen { get; set; }
        }
        public class PortInfo : INotifyPropertyChanged
        {
            private bool _isActive;

            public string Caption { get; set; }
            public string PortName { get; set; }    // e.g., COM3
            public string Description { get; set; } // text before (COMx)
            public string Manufacturer { get; set; }
            public string PnpDeviceId { get; set; }

            public bool IsActive
            {
                get => _isActive;
                set
                {
                    if (_isActive != value)
                    {
                        _isActive = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly SerialPort _serialPort;
        public bool IsOpen => _serialPort.IsOpen;

        public event Action<List<string>> PortListRefreshed;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> DataReceived;
        private ManagementEventWatcher _deviceWatcher;
        public event Action PortsChanged;

        public SerialPortManager()
        {
            _serialPort = new SerialPort();
            // CRITICAL: GRBL expects pure ASCII. UTF-8 would cause byte count mismatch
            // between buffer calculation (ASCII) and actual transmission (UTF-8),
            // leading to buffer overflow and corrupted G-code commands (error:33).
            // Using ASCII encoding - same as OpenBuilds CONTROL.
            _serialPort.Encoding = Encoding.ASCII;
            _serialPort.DataReceived += SerialPort_DataReceived;
            StartDeviceWatcher();
        }

        private void StartDeviceWatcher()
        {
            try
            {
                _deviceWatcher = new ManagementEventWatcher(
                    "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
                _deviceWatcher.EventArrived += (s, e) => { PortsChanged?.Invoke(); };
                _deviceWatcher.Start();
            }
            catch (Exception)
            {
                // Silent fail
            }
        }

        ~SerialPortManager()
        {
            try { _deviceWatcher?.Stop(); } catch { }
            _deviceWatcher?.Dispose();
        }

        public void RefreshPortList()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'");
                var portList = new List<string>();
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    if (queryObj["Caption"] != null) portList.Add(queryObj["Caption"].ToString());
                }
                PortListRefreshed?.Invoke(portList);
            }
            catch
            {
                PortListRefreshed?.Invoke(new List<string>(SerialPort.GetPortNames()));
            }
        }

        public List<PortInfo> GetDetailedPortInfos()
        {
            var result = new List<PortInfo>();
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Caption, PNPDeviceID, Manufacturer FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'");
                foreach (var obj in searcher.Get().OfType<ManagementObject>())
                {
                    string caption = obj["Caption"]?.ToString() ?? string.Empty;
                    string manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                    string pnp = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                    string portName = ExtractPortName(caption);
                    string description = ExtractDescription(caption);

                    result.Add(new PortInfo
                    {
                        Caption = caption,
                        PortName = portName,
                        Description = description,
                        Manufacturer = manufacturer,
                        PnpDeviceId = pnp,
                        IsActive = false
                    });
                }
            }
            catch (ManagementException)
            {
                // Fallback to basic port names if WMI fails
                foreach (var pn in SerialPort.GetPortNames())
                {
                    result.Add(new PortInfo
                    {
                        Caption = pn,
                        PortName = pn,
                        Description = pn,
                        Manufacturer = string.Empty,
                        PnpDeviceId = string.Empty,
                        IsActive = false
                    });
                }
            }
            return result.OrderBy(x => x.PortName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ExtractPortName(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return string.Empty;
            int startIndex = caption.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            if (startIndex >= 0)
            {
                int endIndex = caption.IndexOf(')', startIndex);
                if (endIndex > startIndex)
                {
                    return caption.Substring(startIndex + 1, endIndex - startIndex - 1);
                }
            }
            // Fallback e.g. caption might be just COM3
            if (caption.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return caption;
            return string.Empty;
        }

        private static string ExtractDescription(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return string.Empty;
            int startIndex = caption.LastIndexOf(" (COM", StringComparison.OrdinalIgnoreCase);
            if (startIndex > 0)
            {
                return caption.Substring(0, startIndex).Trim();
            }
            // If no pattern, just return caption
            return caption;
        }

        public async Task<bool> ConnectAsync(string portName, int baudRate)
        {
            if (IsOpen) Disconnect();
            try
            {
                _serialPort.PortName = portName;
                _serialPort.BaudRate = baudRate;
                // Ensure common sane defaults; callers can change later if needed
                _serialPort.Parity = Parity.None;
                _serialPort.DataBits = 8;
                _serialPort.StopBits = StopBits.One;
                _serialPort.Handshake = Handshake.None;
                await Task.Run(() => _serialPort.Open());
                ConnectionStatusChanged?.Invoke(true);
                return true;
            }
            catch
            {
                ConnectionStatusChanged?.Invoke(false);
                return false;
            }
        }

        public void Disconnect()
        {
            if (IsOpen)
            {
                try { _serialPort.Close(); }
                catch { }
                finally { ConnectionStatusChanged?.Invoke(false); }
            }
        }

        // String-based send (G-code lines). Caller is responsible for appending line endings if required.
        // OPTIMIZED: Synchronous write like OpenBuilds - no Task.Run overhead, no debug logging during streaming
        public Task SendDataAsync(string data)
        {
            if (IsOpen)
            {
                // Direct synchronous write - fastest path like OpenBuilds: port.write(gcode)
                _serialPort.Write(data);
            }
            return Task.CompletedTask;
        }

        // RAW byte send for realtime commands (no CR/LF, single byte)
        // OPTIMIZED: Synchronous write - no Task.Run overhead for realtime commands
        public Task SendRawByteAsync(byte b)
        {
            if (!IsOpen) return Task.CompletedTask;
            // Direct synchronous write - fastest path
            _serialPort.Write(new byte[] { b }, 0, 1);
            return Task.CompletedTask;
        }

        // RAW bytes send for small buffers (no CR/LF)
        // OPTIMIZED: Synchronous write
        public Task SendRawBytesAsync(byte[] buffer)
        {
            if (!IsOpen || buffer == null || buffer.Length == 0) return Task.CompletedTask;
            _serialPort.Write(buffer, 0, buffer.Length);
            return Task.CompletedTask;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try { DataReceived?.Invoke(_serialPort.ReadExisting()); } catch { }
        }

        public List<string> GetDetailedPortList()
        {
            var portList = new List<string>();
            try
            {
                var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'");
                foreach (var queryObj in searcher.Get().OfType<ManagementObject>())
                {
                    if (queryObj["Caption"] != null)
                    {
                        portList.Add(queryObj["Caption"].ToString());
                    }
                }
            }
            catch (ManagementException)
            {
                portList.AddRange(SerialPort.GetPortNames());
            }
            return portList;
        }

        public ConnectionDetails GetConnectionDetails()
        {
            try
            {
                return new ConnectionDetails
                {
                    PortName = _serialPort.PortName,
                    BaudRate = _serialPort.BaudRate,
                    Parity = _serialPort.Parity,
                    DataBits = _serialPort.DataBits,
                    StopBits = _serialPort.StopBits,
                    Handshake = _serialPort.Handshake,
                    DtrEnable = _serialPort.DtrEnable,
                    RtsEnable = _serialPort.RtsEnable,
                    ReadTimeout = _serialPort.ReadTimeout,
                    WriteTimeout = _serialPort.WriteTimeout,
                    ReadBufferSize = _serialPort.ReadBufferSize,
                    WriteBufferSize = _serialPort.WriteBufferSize,
                    EncodingName = _serialPort.Encoding?.WebName ?? string.Empty,
                    IsOpen = _serialPort.IsOpen
                };
            }
            catch
            {
                return new ConnectionDetails { IsOpen = false };
            }
        }
    }
}