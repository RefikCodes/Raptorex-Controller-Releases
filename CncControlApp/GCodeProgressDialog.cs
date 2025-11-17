using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CncControlApp
{
    /// <summary>
    /// Progress dialog specifically for G-code file loading and drawing operations
    /// </summary>
    public class GCodeProgressDialog : Window
    {
        #region Fields

        private TextBlock _statusText;
        private TextBlock _progressText;
        private ProgressBar _progressBar;
        private bool _isIndeterminate = true;

        #endregion

        #region Constructor

        public GCodeProgressDialog()
        {
            InitializeComponent();
        }

        #endregion

        #region Initialization

        private void InitializeComponent()
        {
            // Window properties
            Title = "G-Code İşlemi";
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            AllowsTransparency = true;
            Topmost = true;
            ShowInTaskbar = false;

            // Main border
            var mainBorder = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(10)
            };

            // Main grid
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Status text
            _statusText = new TextBlock
            {
                Text = "G-Code dosyası işleniyor...",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 33, 33)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10)
            };
            Grid.SetRow(_statusText, 0);

            // Progress text
            _progressText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 10)
            };
            Grid.SetRow(_progressText, 1);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Height = 20,
                Margin = new Thickness(20, 0, 20, 10),
                IsIndeterminate = true,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204))
            };
            Grid.SetRow(_progressBar, 2);

            // Info text
            var infoText = new TextBlock
            {
                Text = "Lütfen bekleyin... İşlem tamamlanana kadar başka bir işlem yapmayın.",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 20)
            };
            Grid.SetRow(infoText, 3);

            grid.Children.Add(_statusText);
            grid.Children.Add(_progressText);
            grid.Children.Add(_progressBar);
            grid.Children.Add(infoText);

            mainBorder.Child = grid;
            Content = mainBorder;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the status message
        /// </summary>
        /// <param name="status">Status message</param>
        public void UpdateStatus(string status)
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_statusText != null)
                        {
                            _statusText.Text = status;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateStatus error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Update the progress text
        /// </summary>
        /// <param name="progress">Progress message</param>
        public void UpdateProgress(string progress)
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_progressText != null)
                        {
                            _progressText.Text = progress;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateProgress error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Set determinate progress (0-100)
        /// </summary>
        /// <param name="percentage">Progress percentage (0-100)</param>
        public void SetProgress(double percentage)
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_progressBar != null)
                        {
                            _progressBar.IsIndeterminate = false;
                            _progressBar.Value = Math.Max(0, Math.Min(100, percentage));
                            _isIndeterminate = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetProgress error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Set indeterminate progress (animated)
        /// </summary>
        public void SetIndeterminate()
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_progressBar != null)
                        {
                            _progressBar.IsIndeterminate = true;
                            _isIndeterminate = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetIndeterminate error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Safely close the dialog
        /// </summary>
        public void CloseDialog()
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        this.Close();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GCodeProgressDialog close error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Reset progress bar to initial state
        /// </summary>
        public void ResetProgress()
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_progressBar != null)
                        {
                            _progressBar.IsIndeterminate = true;
                            _progressBar.Value = 0;
                            _isIndeterminate = true;
                        }
                        
                        if (_progressText != null)
                        {
                            _progressText.Text = "";
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ResetProgress error: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Show completion message and auto-close after delay
        /// </summary>
        /// <param name="message">Completion message</param>
        /// <param name="autoCloseDelayMs">Auto-close delay in milliseconds</param>
        public void ShowCompletionAndAutoClose(string message, int autoCloseDelayMs = 1000)
        {
            try
            {
                UpdateStatus("Tamamlandı!");
                UpdateProgress(message);
                SetProgress(100);

                // Auto-close after delay
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(autoCloseDelayMs)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    CloseDialog();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowCompletionAndAutoClose error: {ex.Message}");
                CloseDialog(); // Fallback to immediate close
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Check if progress bar is in indeterminate mode
        /// </summary>
        public bool IsIndeterminate => _isIndeterminate;

        /// <summary>
        /// Get current progress value (0-100)
        /// </summary>
        public double CurrentProgress
        {
            get
            {
                if (_progressBar != null && !_progressBar.IsIndeterminate)
                {
                    return _progressBar.Value;
                }
                return 0;
            }
        }

        #endregion

        #region Window Events

        /// <summary>
        /// Handle window loaded event
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Center window on screen if owner is not available
            if (Owner == null)
            {
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = (screenWidth - Width) / 2;
                Top = (screenHeight - Height) / 2;
            }
        }

        /// <summary>
        /// Prevent closing via Alt+F4 or X button during operation
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Allow programmatic closing only
            // User cannot close this dialog manually during operation
            try
            {
                base.OnClosing(e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnClosing error: {ex.Message}");
            }
        }

        #endregion
    }
}