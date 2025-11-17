using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CncControlApp.Managers
{
    /// <summary>
    /// UI Properties Manager - UI ile ilgili tüm property'leri yönetir
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class UIPropertiesManager : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        private string _loadedGCodeFile = "";
        private string _machineStatus = "Idle";
        private readonly ObservableCollection<string> _gCodeLines = new ObservableCollection<string>();

        #endregion

        #region Properties

        /// <summary>
        /// Log mesajları koleksiyonu
        /// </summary>
        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        /// <summary>
        /// G-Code satırları koleksiyonu
        /// </summary>
        public ObservableCollection<string> GCodeLines => _gCodeLines;

        /// <summary>
        /// Yüklenen G-Code dosya adı
        /// </summary>
        public string LoadedGCodeFile
        {
            get => _loadedGCodeFile;
            set
            {
                if (_loadedGCodeFile != value)
                {
                    _loadedGCodeFile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsGCodeLoaded));
                }
            }
        }

        /// <summary>
        /// G-Code dosyası yüklenmiş mi?
        /// </summary>
        public bool IsGCodeLoaded => _gCodeLines.Count > 0;

        /// <summary>
        /// Makine durumu
        /// </summary>
        public string MachineStatus
        {
            get => _machineStatus;
            set
            {
                if (_machineStatus != value)
                {
                    _machineStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Makine durumu nesnesi
        /// </summary>
        public MachineStatus MStatus { get; } = new MachineStatus();

        #endregion

        #region Events

        /// <summary>
        /// Buffer durumu değiştiğinde tetiklenir
        /// </summary>
        public event EventHandler BufferStatusChanged;

        #endregion

        #region Constructor

        /// <summary>
        /// UIPropertiesManager constructor
        /// </summary>
        public UIPropertiesManager()
        {
            // MStatus property changed events'i yönlendir
            MStatus.PropertyChanged += OnMachineStatusPropertyChanged;
        }

        #endregion

        #region Methods

        /// <summary>
        /// G-Code satırlarını temizle
        /// </summary>
        public void ClearGCodeLines()
        {
            _gCodeLines.Clear();
            LoadedGCodeFile = "";
            OnPropertyChanged(nameof(IsGCodeLoaded));
        }

        /// <summary>
        /// G-Code satırı ekle
        /// </summary>
        /// <param name="line">Eklenecek satır</param>
        public void AddGCodeLine(string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _gCodeLines.Add(line.Trim());
                OnPropertyChanged(nameof(IsGCodeLoaded));
            }
        }

        /// <summary>
        /// Birden fazla G-Code satırı ekle
        /// </summary>
        /// <param name="lines">Eklenecek satırlar</param>
        public void AddGCodeLines(string[] lines)
        {
            if (lines == null) return;

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _gCodeLines.Add(line.Trim());
                }
            }
            OnPropertyChanged(nameof(IsGCodeLoaded));
        }

        /// <summary>
        /// Log mesajlarını temizle
        /// </summary>
        public void ClearLogMessages()
        {
            LogMessages.Clear();
        }

        /// <summary>
        /// Buffer durumu değişikliğini bildir
        /// </summary>
        public void NotifyBufferStatusChanged()
        {
            BufferStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// G-Code dosya yükleme durumunu güncelle
        /// </summary>
        /// <param name="fileName">Dosya adı</param>
        /// <param name="validLineCount">Geçerli satır sayısı</param>
        public void UpdateGCodeLoadStatus(string fileName, int validLineCount)
        {
            LoadedGCodeFile = fileName;
            OnPropertyChanged(nameof(IsGCodeLoaded));
        }

        /// <summary>
        /// UI durumunu sıfırla
        /// </summary>
        public void ResetToDefaults()
        {
            ClearGCodeLines();
            ClearLogMessages();
            MachineStatus = "Idle";
            
            // MStatus'u sıfırla
            MStatus.Reset();
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// MachineStatus property changed handler
        /// </summary>
        private void OnMachineStatusPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // MStatus'tan gelen property changed eventi'leri dışarı yönlendir
            OnPropertyChanged($"MStatus.{e.PropertyName}");
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Property changed event
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Property changed notification
        /// </summary>
        /// <param name="propertyName">Property name</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Event'leri temizle
                if (MStatus != null)
                {
                    MStatus.PropertyChanged -= OnMachineStatusPropertyChanged;
                }

                // Koleksiyonları temizle
                LogMessages?.Clear();
                _gCodeLines?.Clear();

                System.Diagnostics.Debug.WriteLine("UIPropertiesManager disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIPropertiesManager dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}