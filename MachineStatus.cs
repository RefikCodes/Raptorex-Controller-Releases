using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CncControlApp
{
    public class MachineStatus : INotifyPropertyChanged
    {
        #region Machine Position Fields
        private double _x = 0.0;
        private double _y = 0.0;
        private double _z = 0.0;
        private double _a = 0.0;
        #endregion

        #region Work Position Fields
        private double _workX = 0.0;
        private double _workY = 0.0;
        private double _workZ = 0.0;
        private double _workA = 0.0;
        #endregion

        #region Runtime Fields
        // Live feed rate (parsed from FS: feed,spindle)
        private double _currentFeed = 0.0;
        // Live spindle speed (parsed from FS: feed,spindle)
        private double _currentSpindle = 0.0;
        #endregion

        #region Machine Position Properties
        public double X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) > 0.001)
                {
                    _x = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (Math.Abs(_y - value) > 0.001)
                {
                    _y = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Z
        {
            get => _z;
            set
            {
                if (Math.Abs(_z - value) > 0.001)
                {
                    _z = value;
                    OnPropertyChanged();
                }
            }
        }

        public double A
        {
            get => _a;
            set
            {
                if (Math.Abs(_a - value) > 0.001)
                {
                    _a = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Work Position Properties
        public double WorkX
        {
            get => _workX;
            set
            {
                if (Math.Abs(_workX - value) > 0.001)
                {
                    _workX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double WorkY
        {
            get => _workY;
            set
            {
                if (Math.Abs(_workY - value) > 0.001)
                {
                    _workY = value;
                    OnPropertyChanged();
                }
            }
        }

        public double WorkZ
        {
            get => _workZ;
            set
            {
                if (Math.Abs(_workZ - value) > 0.001)
                {
                    _workZ = value;
                    OnPropertyChanged();
                }
            }
        }

        public double WorkA
        {
            get => _workA;
            set
            {
                if (Math.Abs(_workA - value) > 0.001)
                {
                    _workA = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Accessory State Fields
        private bool _isSpindleOn = false;
        private bool _isCoolantOn = false;
        #endregion
        
        #region Pin State Fields (from |Pn: field)
        private bool _isXLimitTriggered = false;
        private bool _isYLimitTriggered = false;
        private bool _isZLimitTriggered = false;
        private bool _isProbeTriggered = false;
        #endregion
        
        /// <summary>
        /// True if this status report contains authoritative accessory info (|Ov: field present).
        /// When false, spindle/coolant state should not be updated from this report.
        /// </summary>
        public bool HasAccessoryInfo { get; set; } = false;
        
        /// <summary>
        /// True if this status report contains pin state info (|Pn: field present).
        /// </summary>
        public bool HasPinInfo { get; set; } = false;

        #region Runtime Properties
        /// <summary>
        /// Current real-time feed rate (mm/min) reported by controller (FS: feed,spindle)
        /// </summary>
        public double CurrentFeed
        {
            get => _currentFeed;
            set
            {
                // Tolerans düşük tutulsun ki hızlı güncellensin
                if (Math.Abs(_currentFeed - value) > 0.1)
                {
                    _currentFeed = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Current real-time spindle speed (RPM) reported by controller (FS: feed,spindle)
        /// </summary>
        public double CurrentSpindle
        {
            get => _currentSpindle;
            set
            {
                // Tolerans düşük tutulsun ki hızlı güncellensin
                if (Math.Abs(_currentSpindle - value) > 0.1)
                {
                    _currentSpindle = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Spindle state from status report (A: field contains S for CW, C for CCW)
        /// </summary>
        public bool IsSpindleOn
        {
            get => _isSpindleOn;
            set
            {
                if (_isSpindleOn != value)
                {
                    _isSpindleOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Coolant state from status report (A: field contains F for Flood, M for Mist)
        /// </summary>
        public bool IsCoolantOn
        {
            get => _isCoolantOn;
            set
            {
                if (_isCoolantOn != value)
                {
                    _isCoolantOn = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// X limit switch triggered (from |Pn: field contains X)
        /// </summary>
        public bool IsXLimitTriggered
        {
            get => _isXLimitTriggered;
            set
            {
                if (_isXLimitTriggered != value)
                {
                    _isXLimitTriggered = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Y limit switch triggered (from |Pn: field contains Y)
        /// </summary>
        public bool IsYLimitTriggered
        {
            get => _isYLimitTriggered;
            set
            {
                if (_isYLimitTriggered != value)
                {
                    _isYLimitTriggered = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Z limit switch triggered (from |Pn: field contains Z)
        /// </summary>
        public bool IsZLimitTriggered
        {
            get => _isZLimitTriggered;
            set
            {
                if (_isZLimitTriggered != value)
                {
                    _isZLimitTriggered = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Probe triggered (from |Pn: field contains P)
        /// </summary>
        public bool IsProbeTriggered
        {
            get => _isProbeTriggered;
            set
            {
                if (_isProbeTriggered != value)
                {
                    _isProbeTriggered = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Methods
        public void Reset()
        {
            X = 0;
            Y = 0;
            Z = 0;
            A = 0;
            WorkX = 0;
            WorkY = 0;
            WorkZ = 0;
            WorkA = 0;
            CurrentFeed = 0;
            CurrentSpindle = 0;
            IsSpindleOn = false;
            IsCoolantOn = false;
            IsXLimitTriggered = false;
            IsYLimitTriggered = false;
            IsZLimitTriggered = false;
            IsProbeTriggered = false;
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
