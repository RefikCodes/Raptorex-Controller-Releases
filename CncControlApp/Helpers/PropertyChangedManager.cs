using System;
using System.ComponentModel;
using System.Collections.Generic;

namespace CncControlApp.Helpers
{
    /// <summary>
    /// Helper to manage PropertyChanged event subscriptions and reduce boilerplate
   /// </summary>
    public class PropertyChangedManager : IDisposable
    {
  private readonly List<(INotifyPropertyChanged source, PropertyChangedEventHandler handler)> _subscriptions 
        = new List<(INotifyPropertyChanged, PropertyChangedEventHandler)>();
        private bool _disposed;

   /// <summary>
        /// Subscribe to PropertyChanged with automatic cleanup
   /// </summary>
        public void Subscribe(
  INotifyPropertyChanged source,
      Action<string> onAnyPropertyChanged)
 {
      if (source == null) return;

 PropertyChangedEventHandler handler = (s, e) =>
   {
    try
 {
     onAnyPropertyChanged?.Invoke(e.PropertyName);
 }
   catch (Exception ex)
    {
   System.Diagnostics.Debug.WriteLine($"PropertyChanged handler error: {ex.Message}");
            }
 };

    source.PropertyChanged += handler;
            _subscriptions.Add((source, handler));
   }

     /// <summary>
      /// Subscribe to specific properties only
   /// </summary>
   public void SubscribeToProperties(
   INotifyPropertyChanged source,
 Dictionary<string, Action> propertyHandlers)
 {
   if (source == null || propertyHandlers == null) return;

   PropertyChangedEventHandler handler = (s, e) =>
   {
    try
    {
       if (propertyHandlers.TryGetValue(e.PropertyName, out var action))
      {
      action?.Invoke();
  }
     }
 catch (Exception ex)
 {
                System.Diagnostics.Debug.WriteLine($"PropertyChanged handler error: {ex.Message}");
    }
   };

 source.PropertyChanged += handler;
  _subscriptions.Add((source, handler));
  }

  /// <summary>
  /// Subscribe to PropertyChanged with UI thread marshalling
        /// </summary>
        public void SubscribeWithUiDispatch(
 INotifyPropertyChanged source,
     Action<string> onAnyPropertyChanged)
        {
 if (source == null) return;

PropertyChangedEventHandler handler = (s, e) =>
      {
     try
      {
    UiHelper.RunOnUi(() => onAnyPropertyChanged?.Invoke(e.PropertyName));
       }
       catch (Exception ex)
       {
   System.Diagnostics.Debug.WriteLine($"PropertyChanged handler error: {ex.Message}");
      }
  };

     source.PropertyChanged += handler;
  _subscriptions.Add((source, handler));
        }

   /// <summary>
     /// Unsubscribe from all tracked PropertyChanged events
    /// </summary>
    public void UnsubscribeAll()
  {
       foreach (var (source, handler) in _subscriptions)
       {
 try
   {
       source.PropertyChanged -= handler;
        }
 catch (Exception ex)
  {
 System.Diagnostics.Debug.WriteLine($"Unsubscribe error: {ex.Message}");
       }
   }

        _subscriptions.Clear();
 }

        /// <summary>
    /// Get count of active subscriptions
     /// </summary>
  public int SubscriptionCount => _subscriptions?.Count ?? 0;

        /// <summary>
  /// Create a scoped subscription that auto-disposes
 /// </summary>
        public IDisposable CreateScopedSubscription(
          INotifyPropertyChanged source,
      Action<string> onAnyPropertyChanged)
        {
Subscribe(source, onAnyPropertyChanged);
 return new ScopedSubscription(this, source);
        }

        public void Dispose()
 {
      if (_disposed) return;
            _disposed = true;
   UnsubscribeAll();
  }

  private class ScopedSubscription : IDisposable
  {
  private readonly PropertyChangedManager _manager;
 private readonly INotifyPropertyChanged _source;
       private bool _disposed;

       public ScopedSubscription(PropertyChangedManager manager, INotifyPropertyChanged source)
  {
 _manager = manager;
     _source = source;
   }

  public void Dispose()
  {
       if (_disposed) return;
      _disposed = true;
    // Remove only this source's subscriptions
       _manager._subscriptions.RemoveAll(s => s.source == _source);
         }
        }
    }

    /// <summary>
 /// Extension methods for INotifyPropertyChanged
        /// </summary>
    public static class PropertyChangedExtensions
        {
        /// <summary>
  /// Subscribe to PropertyChanged with fluent syntax
 /// </summary>
  public static IDisposable OnPropertyChanged(
      this INotifyPropertyChanged source,
       Action<string> handler)
   {
   if (source == null || handler == null)
         return new EmptyDisposable();

       PropertyChangedEventHandler eventHandler = (s, e) =>
   {
      try { handler(e.PropertyName); }
    catch { }
     };

    source.PropertyChanged += eventHandler;

     return new DelegateDisposable(() =>
      {
  try { source.PropertyChanged -= eventHandler; }
   catch { }
    });
        }

   /// <summary>
   /// Subscribe to specific property only
        /// </summary>
   public static IDisposable OnPropertyChanged(
  this INotifyPropertyChanged source,
     string propertyName,
            Action handler)
 {
    if (source == null || handler == null || string.IsNullOrEmpty(propertyName))
  return new EmptyDisposable();

  PropertyChangedEventHandler eventHandler = (s, e) =>
        {
     if (e.PropertyName == propertyName)
    {
  try { handler(); }
      catch { }
     }
       };

source.PropertyChanged += eventHandler;

   return new DelegateDisposable(() =>
            {
       try { source.PropertyChanged -= eventHandler; }
     catch { }
            });
        }

        private class EmptyDisposable : IDisposable
        {
   public void Dispose() { }
  }

 private class DelegateDisposable : IDisposable
     {
   private readonly Action _disposeAction;
  private bool _disposed;

 public DelegateDisposable(Action disposeAction)
     {
_disposeAction = disposeAction;
}

   public void Dispose()
       {
    if (_disposed) return;
     _disposed = true;
   try { _disposeAction?.Invoke(); }
          catch { }
     }
   }
    }
}
