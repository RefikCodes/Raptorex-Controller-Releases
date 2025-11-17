# Quick Reference: Using GCodeView Refactored Helpers

## DebounceTimer Helper

### Creating a Debounce Timer
```csharp
// Instead of:
var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
timer.Tick += (s, e) => { 
    timer.Stop(); 
 DoSomething(); 
};

// Use:
var timer = new DebounceTimer(TimeSpan.FromMilliseconds(150), () => DoSomething());
```

### Common Operations
```csharp
// Trigger the debounced action (restarts timer)
timer.Trigger();

// Cancel pending action
timer.Cancel();

// Change interval dynamically
timer.SetInterval(TimeSpan.FromMilliseconds(100));

// Check if timer is active
if (timer.IsActive)
{
    // Timer is running
}

// Cleanup (implements IDisposable)
timer.Dispose();
```

## UiHelper Methods

### Safe UI Updates
```csharp
// Instead of:
Application.Current.Dispatcher.BeginInvoke(new Action(() =>
{
    if (MyTextBlock != null)
    {
        MyTextBlock.Text = "Hello";
        MyTextBlock.Foreground = Brushes.Green;
    }
}), DispatcherPriority.Background);

// Use:
UiHelper.SafeUpdateTextBlock(MyTextBlock, "Hello", Brushes.Green);
```

### Generic UI Actions
```csharp
// Run any action on UI thread
UiHelper.RunOnUi(() =>
{
    // Your UI update code
 MyLabel.Text = "Updated";
}, DispatcherPriority.Background); // Priority is optional
```

### Control State Management
```csharp
// Enable/disable controls safely
UiHelper.SafeSetEnabled(MyButton, true);

// Set visibility
UiHelper.SafeSetVisibility(MyPanel, Visibility.Collapsed);
```

### Formatting Helpers
```csharp
// Format time (minutes to human-readable)
string timeStr = UiHelper.FormatTime(125.5); // "2h 5min"
string quickStr = UiHelper.FormatTime(0.5);  // "30sec"

// Format file size
string sizeStr = UiHelper.FormatFileSize(1024000); // "1000.0 KB"

// Get status color brush
var brush = UiHelper.GetStatusBrush("Idle");  // Green
var brush2 = UiHelper.GetStatusBrush("Alarm"); // Red
```

## StatusBarManager

### Creating the Manager
```csharp
var statusMgr = new StatusBarManager(
    getPartDimensions: () => (_currentPartWidth, _currentPartHeight),
    getTableDimensions: () => (_tableDimensionsLoaded, _tableMaxX, _tableMaxY),
    getCurrentRotationAngle: () => _currentRotationAngle,
    getEnableFitOnRotation: () => _enableFitOnRotation,
    getIsFileLoaded: () => _fileService?.IsFileLoaded == true
);
```

### Getting Status Info
```csharp
var (partSizeText, fitStatusText, fitColor, fitTooltip, originText) = 
    statusMgr.GetStatusBarInfo();

// Apply to UI
UiHelper.SafeUpdateTextBlock(PartSizeTextBlock, partSizeText);
UiHelper.SafeUpdateTextBlock(FitStatusTextBlock, fitStatusText, new SolidColorBrush(fitColor));
FitStatusTextBlock.ToolTip = fitTooltip;
UiHelper.SafeUpdateTextBlock(OriginStatusTextBlock, originText);
```

## Migration Patterns

### Pattern 1: Dispatcher Calls
```csharp
// OLD:
Application.Current.Dispatcher.BeginInvoke(new Action(() =>
{
    StatusLabel.Text = "Ready";
}), DispatcherPriority.Background);

// NEW:
UiHelper.SafeUpdateTextBlock(StatusLabel, "Ready");
```

### Pattern 2: Timer Setup
```csharp
// OLD:
var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
timer.Tick += async (s, e) =>
{
    timer.Stop();
    await ProcessData();
};

// NEW:
var timer = new DebounceTimer(
    TimeSpan.FromMilliseconds(200), 
    async () => await ProcessData()
);
```

### Pattern 3: Conditional Slider Updates
```csharp
// OLD:
if (IsExecutionActive)
{
    timer.Stop();
    timer.Interval = _touchActive ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(80);
    timer.Start();
}

// NEW:
timer.SetInterval(_touchActive ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(80));
timer.Trigger();
```

### Pattern 4: Multiple UI Updates
```csharp
// OLD:
Application.Current.Dispatcher.BeginInvoke(new Action(() =>
{
    if (XDistanceTextBlock != null) XDistanceTextBlock.Text = $"{xRange:F1}mm";
    if (YDistanceTextBlock != null) YDistanceTextBlock.Text = $"{yRange:F1}mm";
    if (ZDistanceTextBlock != null) ZDistanceTextBlock.Text = $"{zRange:F1}mm";
}), DispatcherPriority.Background);

// NEW:
UiHelper.RunOnUi(() =>
{
    UiHelper.SafeUpdateTextBlock(XDistanceTextBlock, $"{xRange:F1}mm");
    UiHelper.SafeUpdateTextBlock(YDistanceTextBlock, $"{yRange:F1}mm");
    UiHelper.SafeUpdateTextBlock(ZDistanceTextBlock, $"{zRange:F1}mm");
});
```

## Best Practices

### ✅ DO:
- Use `UiHelper.RunOnUi()` for all UI thread operations
- Use `DebounceTimer` for throttled/debounced operations
- Dispose of `DebounceTimer` instances when done
- Use helper formatting methods (`FormatTime`, `FormatFileSize`)

### ❌ DON'T:
- Don't mix raw `DispatcherTimer` with `DebounceTimer` for similar operations
- Don't create duplicate formatting logic
- Don't call `Dispatcher.BeginInvoke` directly when helpers are available
- Don't forget to check `IsActive` before assuming timer state

## Common Scenarios

### Scenario 1: Slider with Debounced Updates
```csharp
private DebounceTimer _mySliderTimer;

public MyControl()
{
    _mySliderTimer = new DebounceTimer(
     TimeSpan.FromMilliseconds(150),
        async () => await ApplySliderValue(_pendingValue)
    );
}

private void MySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    _pendingValue = (int)e.NewValue;
    _mySliderTimer.Trigger(); // Restart debounce
}
```

### Scenario 2: Touch-Responsive Slider
```csharp
private void MySlider_TouchDown(object sender, TouchEventArgs e)
{
    _isTouchActive = true;
    _mySliderTimer.SetInterval(TimeSpan.FromMilliseconds(50)); // Faster updates for touch
}

private void MySlider_TouchUp(object sender, TouchEventArgs e)
{
_isTouchActive = false;
  _mySliderTimer.Cancel();
    await ApplySliderValue(_pendingValue); // Apply immediately on release
}
```

### Scenario 3: Batch UI Updates
```csharp
private void UpdateStatistics(FileStats stats)
{
  UiHelper.RunOnUi(() =>
    {
 UiHelper.SafeUpdateTextBlock(FileSizeLabel, UiHelper.FormatFileSize(stats.Size));
        UiHelper.SafeUpdateTextBlock(LineCountLabel, stats.LineCount.ToString());
  UiHelper.SafeUpdateTextBlock(DurationLabel, UiHelper.FormatTime(stats.Duration));
    });
}
```

## Performance Tips

1. **Batch UI updates** using a single `RunOnUi()` call instead of multiple
2. **Adjust debounce intervals** based on responsiveness needs (50-200ms typical)
3. **Cancel timers** when no longer needed to free resources
4. **Use appropriate priority** for dispatcher calls (Background for non-critical updates)

## Troubleshooting

### Timer not firing?
```csharp
// Check if timer is active
if (!_myTimer.IsActive)
{
    _myTimer.Trigger(); // Restart
}
```

### UI not updating?
```csharp
// Ensure you're using helper methods
UiHelper.RunOnUi(() => {
    // Your update code
});

// Or for simple text updates:
UiHelper.SafeUpdateTextBlock(MyLabel, "Text");
```

### Memory leak concerns?
```csharp
// Always dispose timers in cleanup methods
protected override void OnUnloaded(RoutedEventArgs e)
{
    _myTimer?.Dispose();
    base.OnUnloaded(e);
}
```
