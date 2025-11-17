using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CncControlApp.Services
{
    /// <summary>
    /// Step Control Service - XYZ ve A-ekseni için ayrı step kontrol yönetimi
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class StepControlService : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        // ✅ Step control fields for XYZ and A-axis
        private bool _isXYZStepMode = false;
        private bool _isAStepMode = false;
        private double _selectedXYZStepSize = 1.0;
        private double _selectedAStepSize = 1.0;

        // ✅ A-axis specific speed control
        private double _aAxisSpeedPercentage = 50.0;

        // ✅ Error handling delegate - MainControll'dan gelecek
        private Action<string, Exception> _logErrorDelegate;
        private Action<string> _addLogMessageDelegate;

        #endregion

        #region Constructor

        public StepControlService()
        {
            InitializeStepSettings();
        }

        public StepControlService(Action<string, Exception> logErrorDelegate, Action<string> addLogMessageDelegate)
        {
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
            InitializeStepSettings();
        }

        #endregion

        #region XYZ Step Control Properties

        /// <summary>
        /// XYZ eksenleri için step mode aktif mi?
        /// </summary>
        public bool IsXYZStepMode
        {
            get => _isXYZStepMode;
            set
            {
                if (_isXYZStepMode != value)
                {
                    _isXYZStepMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(XYZModeDisplayText));
                    OnPropertyChanged(nameof(XYZModeDescriptionText));
                    OnPropertyChanged(nameof(SelectedXYZStepSizeDisplay)); // add this

                    // Log the change
                    var modeText = value ? "STEP" : "CONTINUOUS";
                    _addLogMessageDelegate?.Invoke($"> XYZ hareket modu: {modeText} (Step Size: {SelectedXYZStepSize:F3}mm)");
                }
            }
        }

        /// <summary>
        /// XYZ eksenleri için step boyutu (mm)
        /// </summary>
        public double SelectedXYZStepSize
        {
            get => _selectedXYZStepSize;
            set
            {
                if (Math.Abs(_selectedXYZStepSize - value) > 0.0001)
                {
                    _selectedXYZStepSize = Math.Max(0.001, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedXYZStepSizeDisplay));
                }
            }
        }

        /// <summary>
        /// XYZ step size görüntü metni - Continuous mode'da infinite symbol
        /// </summary>
        public string SelectedXYZStepSizeDisplay => IsXYZStepMode ? $"{SelectedXYZStepSize:0.###} mm" : "∞ mm";

        /// <summary>
        /// XYZ mode görüntü metni (STEP/CONTINUOUS)
        /// </summary>
        public string XYZModeDisplayText => IsXYZStepMode ? "STEP" : "CONTINUOUS";

        /// <summary>
        /// XYZ mode açıklama metni
        /// </summary>
        public string XYZModeDescriptionText => IsXYZStepMode ? "XYZ Step Mode" : "XYZ Continuous";

        #endregion

        #region A-Axis Step Control Properties

        /// <summary>
        /// A-ekseni için step mode aktif mi?
        /// </summary>
        public bool IsAStepMode
        {
            get => _isAStepMode;
            set
            {
                if (_isAStepMode != value)
                {
                    _isAStepMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AModeDisplayText));
                    OnPropertyChanged(nameof(AModeDescriptionText));
                    OnPropertyChanged(nameof(SelectedAStepSizeDisplay)); // add this

                    // Log the change
                    var modeText = value ? "STEP" : "CONTINUOUS";
                    _addLogMessageDelegate?.Invoke($"> A-ekseni hareket modu: {modeText}");
                }
            }
        }

        /// <summary>
        /// A-ekseni için step boyutu (derece)
        /// </summary>
        public double SelectedAStepSize
        {
            get => _selectedAStepSize;
            set
            {
                if (Math.Abs(_selectedAStepSize - value) > 0.0001)
                {
                    _selectedAStepSize = Math.Max(0.001, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedAStepSizeDisplay));
                }
            }
        }

        /// <summary>
        /// A-axis step size görüntü metni - Continuous mode'da infinite symbol
        /// </summary>
        public string SelectedAStepSizeDisplay => IsAStepMode ? $"{SelectedAStepSize:0.###}°" : "∞°";

        /// <summary>
        /// A-axis mode görüntü metni (STEP/CONTINUOUS)
        /// </summary>
        public string AModeDisplayText => IsAStepMode ? "STEP" : "CONTINUOUS";

        /// <summary>
        /// A-axis mode açıklama metni
        /// </summary>
        public string AModeDescriptionText => IsAStepMode ? "A-Axis Step Mode" : "A-Axis Continuous";

        #endregion

        #region A-Axis Speed Control Properties

        /// <summary>
        /// A-axis specific speed control percentage
        /// </summary>
        public double AAxisSpeedPercentage
        {
            get => _aAxisSpeedPercentage;
            set
            {
                if (Math.Abs(_aAxisSpeedPercentage - value) > 0.1)
                {
                    _aAxisSpeedPercentage = Math.Max(1.0, Math.Min(100.0, value));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AAxisSpeedDisplayText));
                }
            }
        }

        /// <summary>
        /// A-axis hız görüntü metni - MaxAJogSpeed değeri için delegate gerekli
        /// </summary>
        public string AAxisSpeedDisplayText => $"{_aAxisSpeedPercentage:F0}%";

        /// <summary>
        /// A-axis için hesaplanmış jog hızı - MaxAJogSpeed değeri için delegate gerekli
        /// </summary>
        /// <param name="maxAJogSpeed">Maksimum A-axis jog hızı</param>
        /// <returns>Hesaplanmış hız</returns>
        public int GetCurrentAAxisJogSpeed(int maxAJogSpeed)
        {
            return (int)(maxAJogSpeed * _aAxisSpeedPercentage / 100.0);
        }

        /// <summary>
        /// A-axis hız görüntü metnini hesapla
        /// </summary>
        /// <param name="maxAJogSpeed">Maksimum A-axis jog hızı</param>
        /// <returns>Hız görüntü metni</returns>
        public string GetAAxisSpeedDisplayText(int maxAJogSpeed)
        {
            var currentSpeed = GetCurrentAAxisJogSpeed(maxAJogSpeed);
            return $"{_aAxisSpeedPercentage:F0}% ({currentSpeed} deg/min)";
        }

        #endregion

        #region Step Control Methods

        /// <summary>
        /// XYZ step size'ını ayarla
        /// </summary>
        /// <param name="stepSize">Step boyutu (mm)</param>
        public void SetXYZStepSize(double stepSize)
        {
            try
            {
                if (stepSize <= 0)
                {
                    var ex = new ArgumentException($"Step size must be positive: {stepSize}");
                    _logErrorDelegate?.Invoke("SetXYZStepSize", ex);
                    return;
                }

                if (stepSize > 100) // Maximum step size limit
                {
                    var ex = new ArgumentException($"Step size too large: {stepSize} mm (max 100mm)");
                    _logErrorDelegate?.Invoke("SetXYZStepSize", ex);
                    stepSize = 100;
                }

                SelectedXYZStepSize = stepSize;

                // ✅ Debug ve user feedback
                _addLogMessageDelegate?.Invoke($"> XYZ Step Size ayarlandı: {stepSize} mm");
                System.Diagnostics.Debug.WriteLine($"XYZ Step size set to: {stepSize} mm");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("SetXYZStepSize", ex);
            }
        }

        /// <summary>
        /// A-axis step size'ını ayarla
        /// </summary>
        /// <param name="stepSize">Step boyutu (derece)</param>
        public void SetAStepSize(double stepSize)
        {
            try
            {
                if (stepSize <= 0)
                {
                    var ex = new ArgumentException($"Step size must be positive: {stepSize}");
                    _logErrorDelegate?.Invoke("SetAStepSize", ex);
                    return;
                }

                if (stepSize > 360) // Maximum step size limit for rotation
                {
                    var ex = new ArgumentException($"Step size too large: {stepSize}° (max 360°)");
                    _logErrorDelegate?.Invoke("SetAStepSize", ex);
                    stepSize = 360;
                }

                SelectedAStepSize = stepSize;
                System.Diagnostics.Debug.WriteLine($"A-Axis Step size set to: {stepSize}°");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("SetAStepSize", ex);
            }
        }

        /// <summary>
        /// XYZ step mode'unu aç/kapat
        /// </summary>
        public void ToggleXYZStepMode()
        {
            try
            {
                IsXYZStepMode = !IsXYZStepMode;
                System.Diagnostics.Debug.WriteLine($"XYZ Step Mode toggled: {IsXYZStepMode}, Step Size: {SelectedXYZStepSize}");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("ToggleXYZStepMode", ex);
            }
        }

        /// <summary>
        /// A-axis step mode'unu aç/kapat
        /// </summary>
        public void ToggleAStepMode()
        {
            try
            {
                IsAStepMode = !IsAStepMode;
                System.Diagnostics.Debug.WriteLine($"A-Axis Step Mode: {IsAStepMode}");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("ToggleAStepMode", ex);
            }
        }

        /// <summary>
        /// Mevcut step mode bilgilerini al
        /// </summary>
        /// <returns>Step mode durum metni</returns>
        public string GetStepModeInfo()
        {
            try
            {
                return $"XYZ Mode: {(IsXYZStepMode ? "STEP" : "CONTINUOUS")} ({SelectedXYZStepSize:0.###}mm), " +
                       $"A Mode: {(IsAStepMode ? "STEP" : "CONTINUOUS")} ({SelectedAStepSize:0.###}°)";
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("GetStepModeInfo", ex);
                return "Step mode info unavailable";
            }
        }

        /// <summary>
        /// A-axis hız yüzdesini ayarla
        /// </summary>
        /// <param name="percentage">Hız yüzdesi (1-100)</param>
        public void SetAAxisSpeedPercentage(double percentage)
        {
            try
            {
                if (percentage < 1.0 || percentage > 100.0)
                {
                    var ex = new ArgumentException($"A-axis speed percentage must be between 1-100: {percentage}");
                    _logErrorDelegate?.Invoke("SetAAxisSpeedPercentage", ex);
                    return;
                }

                AAxisSpeedPercentage = percentage;
                System.Diagnostics.Debug.WriteLine($"A-Axis speed percentage set to: {percentage}%");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("SetAAxisSpeedPercentage", ex);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Step control ayarlarını başlat
        /// </summary>
        private void InitializeStepSettings()
        {
            try
            {
                // Default values for step controls
                IsXYZStepMode = false;
                IsAStepMode = false;
                SelectedXYZStepSize = 1.0; // 1mm default
                SelectedAStepSize = 1.0;   // 1° default

                // ✅ Initialize A-axis speed control
                AAxisSpeedPercentage = 50.0; // 50% default

                System.Diagnostics.Debug.WriteLine("StepControlService: Step control settings initialized");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("InitializeStepSettings", ex);
            }
        }

        /// <summary>
        /// XYZ eksenlerinin step command'ını oluştur
        /// </summary>
        /// <param name="axis">Eksen (X, Y, Z)</param>
        /// <param name="direction">Yön (+ veya -)</param>
        /// <param name="feedRate">Feed rate</param>
        /// <returns>G-Code komutu</returns>
        public string GenerateXYZStepCommand(string axis, string direction, int feedRate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(axis))
                    throw new ArgumentException("Axis cannot be empty", nameof(axis));

                if (string.IsNullOrWhiteSpace(direction))
                    throw new ArgumentException("Direction cannot be empty", nameof(direction));

                var stepValue = direction == "+" ? SelectedXYZStepSize : -SelectedXYZStepSize;

                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "$J=G91{0}{1:F3} F{2}", axis.ToUpper(), stepValue, feedRate);
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("GenerateXYZStepCommand", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// A-ekseni step command'ını oluştur
        /// </summary>
        /// <param name="direction">Yön (+ veya -)</param>
        /// <param name="feedRate">Feed rate</param>
        /// <returns>G-Code komutu</returns>
        public string GenerateAStepCommand(string direction, int feedRate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(direction))
                    throw new ArgumentException("Direction cannot be empty", nameof(direction));

                var stepValue = direction == "+" ? SelectedAStepSize : -SelectedAStepSize;

                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "$J=G91A{0:F3} F{1}", stepValue, feedRate);
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("GenerateAStepCommand", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Step mode durumunu sıfırla
        /// </summary>
        public void ResetToDefaults()
        {
            try
            {
                IsXYZStepMode = false;
                IsAStepMode = false;
                SelectedXYZStepSize = 1.0;
                SelectedAStepSize = 1.0;
                AAxisSpeedPercentage = 50.0;

                _addLogMessageDelegate?.Invoke("> Step control ayarları varsayılan değerlere sıfırlandı");
                System.Diagnostics.Debug.WriteLine("StepControlService: Settings reset to defaults");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("ResetToDefaults", ex);
            }
        }

        /// <summary>
        /// Mevcut ayarları logla
        /// </summary>
        public void LogCurrentSettings()
        {
            try
            {
                _addLogMessageDelegate?.Invoke($"> === STEP CONTROL AYARLARI ===");
                _addLogMessageDelegate?.Invoke($"> XYZ Mode: {XYZModeDisplayText} ({SelectedXYZStepSizeDisplay})");
                _addLogMessageDelegate?.Invoke($"> A Mode: {AModeDisplayText} ({SelectedAStepSizeDisplay})");
                _addLogMessageDelegate?.Invoke($"> A-Axis Speed: {AAxisSpeedPercentage:F1}%");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("LogCurrentSettings", ex);
            }
        }

        #endregion

        #region Error Handling Support

        /// <summary>
        /// Error delegate'lerini güncelle
        /// </summary>
        /// <param name="logErrorDelegate">Hata loglama delegate'i</param>
        /// <param name="addLogMessageDelegate">Mesaj ekleme delegate'i</param>
        public void UpdateErrorDelegates(Action<string, Exception> logErrorDelegate, Action<string> addLogMessageDelegate)
        {
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                // Clear delegates
                _logErrorDelegate = null;
                _addLogMessageDelegate = null;

                System.Diagnostics.Debug.WriteLine("StepControlService disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StepControlService dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}