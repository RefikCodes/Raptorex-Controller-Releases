using System;
using System.Windows.Threading;

namespace CncControlApp.Helpers
{
    /// <summary>
 /// Reusable debounce timer helper to eliminate duplicate timer management code
  /// </summary>
    public class DebounceTimer : IDisposable
    {
    private readonly DispatcherTimer _timer;
  private readonly Action _action;
        private bool _disposed;

      public DebounceTimer(TimeSpan interval, Action action)
  {
      _action = action ?? throw new ArgumentNullException(nameof(action));
      _timer = new DispatcherTimer { Interval = interval };
    _timer.Tick += OnTimerTick;
  }

        private void OnTimerTick(object sender, EventArgs e)
   {
    try
            {
         _timer.Stop();
            _action();
            }
  catch (Exception ex)
            {
       System.Diagnostics.Debug.WriteLine($"DebounceTimer action error: {ex.Message}");
 }
      }

        /// <summary>
        /// Trigger the debounced action (restarts the timer)
        /// </summary>
        public void Trigger()
     {
            if (_disposed) return;
  _timer.Stop();
      _timer.Start();
        }

    /// <summary>
  /// Cancel any pending action
        /// </summary>
        public void Cancel()
      {
            if (_disposed) return;
            _timer.Stop();
   }

      /// <summary>
        /// Check if timer is currently running
        /// </summary>
        public bool IsActive => !_disposed && _timer.IsEnabled;

    /// <summary>
  /// Change the debounce interval
        /// </summary>
        public void SetInterval(TimeSpan interval)
        {
            if (_disposed) return;
   bool wasRunning = _timer.IsEnabled;
       _timer.Stop();
            _timer.Interval = interval;
      if (wasRunning)
         _timer.Start();
        }

        public void Dispose()
        {
    if (_disposed) return;
      _disposed = true;
       
  try
          {
  _timer.Stop();
      _timer.Tick -= OnTimerTick;
  }
   catch { }
        }
    }
}
