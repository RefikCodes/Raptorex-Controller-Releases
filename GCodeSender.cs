using CncControlApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace CncControlApp
{
    // GCodeSender.cs



    public class GCodeSender
    {
        private readonly SerialPortManager _portManager;
        public event Action<string> ResponseReceived;

        public GCodeSender(SerialPortManager portManager)
        {
            _portManager = portManager ?? throw new ArgumentNullException(nameof(portManager));
            _portManager.DataReceived += OnDataReceivedFromPort;
        }

        public async Task SendCommandAsync(string gcodeCommand)
        {
            if (string.IsNullOrWhiteSpace(gcodeCommand) || !_portManager.IsOpen) return;
            await _portManager.SendDataAsync(gcodeCommand + "\n");
        }

        // Send GRBL/FluidNC realtime command as raw single byte (no CR/LF)
        public async Task SendControlCharacterAsync(char controlCharacter)
        {
            if (!_portManager.IsOpen) return;
            await _portManager.SendRawByteAsync((byte)controlCharacter);
        }

        private void OnDataReceivedFromPort(string data)
        {
            ResponseReceived?.Invoke(data);
        }
    }
}
