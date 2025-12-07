using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CncControlApp
{
    /// <summary>
    /// G-Code line item for ListBox binding with execution status
    /// 🆕 ENHANCED with DEBUG support
    /// </summary>
    public class GCodeLineItem : INotifyPropertyChanged
    {
        private bool _isCurrentLine = false;
        private bool _isExecuted = false;
        private bool _hasError = false;
        private bool _isSent = false;
        private bool _isInfo = false;
        private string _gcodeLine = string.Empty;

        public int LineNumber { get; set; }
        
        public string GCodeLine
        {
            get => _gcodeLine;
            set
            {
                _gcodeLine = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Bilgi satırı mı? (yorum vb.) – sarı ile işaretlenecek
        /// </summary>
        public bool IsInfo
        {
            get => _isInfo;
            set
            {
                if (_isInfo != value)
                {
                    _isInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Şu anda çalıştırılan satır mı?
        /// </summary>
        public bool IsCurrentLine
        {
            get => _isCurrentLine;
            set
            {
                if (_isCurrentLine != value)
                {
                    _isCurrentLine = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Satır gönderildi mi? (OK bekliyor/pending)
        /// </summary>
        public bool IsSent
        {
            get => _isSent;
            set
            {
                if (_isSent != value)
                {
                    _isSent = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Çalıştırılmış satır mı? (OK alınmış)
        /// </summary>
        public bool IsExecuted
        {
            get => _isExecuted;
            private set
            {
                if (_isExecuted != value)
                {
                    _isExecuted = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Hata olan satır mı?
        /// </summary>
        public bool HasError
        {
            get => _hasError;
            set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Execution status'ı sıfırla
        /// </summary>
        public void ResetStatus()
        {
            IsCurrentLine = false;
            IsSent = false;
            IsExecuted = false;
            HasError = false;
            // IsInfo korunur (dosya yüklemede atanıyor)
        }

        /// <summary>
        /// Bu satır gönderildi (OK bekliyor)
        /// </summary>
        public void SetAsSent()
        {
            IsSent = true;
            // current/executed/error değişmez
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Current line olarak işaretle
        /// </summary>
        public void SetAsCurrent()
        {
            IsCurrentLine = true;
            // Current'a girerken pending sayılabilir
            IsSent = true;
            IsExecuted = false;
            HasError = false;
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Executed line olarak işaretle
        /// </summary>
        public void SetAsExecuted()
        {
            IsCurrentLine = false; // Current olmaktan çık
            IsSent = true;         // Gönderilmiş de olur
            IsExecuted = true;     // Executed ol
            HasError = false;      // Error durumundan çık
        }

        /// <summary>
        /// 🆕 ENHANCED DEBUG: Error line olarak işaretle
        /// </summary>
        public void SetAsError()
        {
            IsCurrentLine = false;
            IsSent = true;
            IsExecuted = false;
            HasError = true;
        }

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}