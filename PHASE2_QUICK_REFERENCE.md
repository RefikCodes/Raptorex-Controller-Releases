# Quick Reference: Phase 2 Refactoring Helpers

## EventHandlerHelper Usage

### Basic Async Handler Wrapping
```csharp
// Instead of:
private async void Button_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await DoSomethingAsync();
    }
    catch (Exception ex)
    {
        App.MainController?.AddLogMessage($"> ❌ HATA: {ex.Message}");
    }
}

// Use:
private async void Button_Click(object sender, RoutedEventArgs e)
{
    await EventHandlerHelper.SafeHandleAsync(
        async () => await DoSomethingAsync(),
        "Button_Click",
        App.MainController?.AddLogMessage
    );
}
```

### Batch Jog Button Setup
```csharp
// Instead of 16+ separate handlers:
EventHandlerHelper.CreateJogButtonHandlers(
  new Dictionary<string, (Button button, Func<Task> action)>
    {
        ["JogXPlus"] = (JogXPlusButton, () => _controller.StartJogXPlusAsync()),
        ["JogXMinus"] = (JogXMinusButton, () => _controller.StartJogXMinusAsync()),
   ["JogYPlus"] = (JogYPlusButton, () => _controller.StartJogYPlusAsync()),
        ["JogYMinus"] = (JogYMinusButton, () => _controller.StartJogYMinusAsync()),
        ["JogZPlus"] = (JogZPlusButton, () => _controller.StartJogZPlusAsync()),
        ["JogZMinus"] = (JogZMinusButton, () => _controller.StartJogZMinusAsync()),
        ["JogAPlus"] = (JogAPlusButton, () => _controller.StartJogAPlusAsync()),
        ["JogAMinus"] = (JogAMinusButton, () => _controller.StartJogAMinusAsync())
    },
    App.MainController?.AddLogMessage
);
```

### Touch Event Setup
```csharp
EventHandlerHelper.CreateTouchHandlers(
    MyButton,
    touchStart: async (e) => await StartOperation(),
    touchEnd: async (e) => await StopOperation(),
    "MyButton",
    App.MainController?.AddLogMessage
);
```

### Touch/Mouse Conflict Resolution
```csharp
private bool _touchEventInProgress = false;

private async void Button_MouseDown(object sender, MouseButtonEventArgs e)
{
    if (!EventHandlerHelper.ShouldHandleMouseEvent(e, ref _touchEventInProgress))
    {
        e.Handled = true;
    return;
    }
    
    await DoSomething();
}
```

## ProbeHelper Usage

### Wait for Idle
```csharp
// Instead of manual loop:
if (!await ProbeHelper.WaitForIdleAsync(
    () => App.MainController?.MachineStatus,
    timeoutMs: 15000,
    operationTag: "Probe_Retract",
    logger: App.MainController?.AddLogMessage,
 requiredIdleCount: 3,
    checkIntervalMs: 50))
{
    return false;
}
```

### Validate Probe Measurements
```csharp
// Check if any pair of measurements are within tolerance
double[] measurements = { 10.123, 10.125, 10.180 };
var (valid, tolerance, indexA, indexB) = ProbeHelper.ValidateMeasurements(
    measurements,
    toleranceThreshold: 0.06,
    startIndex: 1 // Start from second measurement
);

if (valid)
{
    double avg = ProbeHelper.AveragePair(measurements[indexA], measurements[indexB]);
    Console.WriteLine($"Valid pair: {indexA}, {indexB} - Average: {avg:F3}, Tolerance: {tolerance:F3}");
}
```

### Calculate Timeouts
```csharp
// For feed movements:
int timeout = ProbeHelper.EstimateTimeoutForFeed(
    distanceMm: 30.0,
    feedMmMin: 100,
    minMs: 8000 // minimum timeout
);

// For rapid movements:
int timeout = ProbeHelper.EstimateTimeoutForRapid(
    distanceMm: 10.0,
    rapidMmMin: 1000.0,
    minMs: 5000
);
```

### Build G-code Commands
```csharp
// Probe command:
string probeCmd = ProbeHelper.BuildProbeCommand('Z', -30.0, 100);
// Result: "G38.2 Z-30.000 F100"

// Retract command:
string retractCmd = ProbeHelper.BuildRetractCommand('Z', 2.0, isRapid: true);
// Result: "G00 Z2.000"

// Axis move:
string moveCmd = ProbeHelper.BuildAxisMove('X', 5.123);
// Result: "X5.123"
```

### Execute Probe Sequence
```csharp
var steps = new[]
{
  new ProbeSequenceStep(
   "G91",
    "⚙️ Switch to relative mode",
      "SetRelative"
    ) { WaitForIdle = false },
    
    new ProbeSequenceStep(
        "G00 Z2.000",
        "🔼 Retract 2mm",
        "Retract"
    ),
  
    new ProbeSequenceStep(
  ProbeHelper.BuildProbeCommand('Z', -30, 100),
      "🔍 Probe down",
        "Probe"
    ) { TimeoutMs = 45000 },
    
    new ProbeSequenceStep(
        "G90",
  "⚙️ Switch to absolute mode",
        "SetAbsolute"
    ) { WaitForIdle = false }
};

bool success = await ProbeHelper.ExecuteProbeSequence(
    App.MainController.SendGCodeCommandWithConfirmationAsync,
    () => App.MainController?.MachineStatus,
    steps,
    App.MainController?.AddLogMessage
);
```

### Coordinate Validation
```csharp
double z = App.MainController?.MStatus?.Z ?? double.NaN;

if (!ProbeHelper.IsFinite(z))
{
    App.MainController?.AddLogMessage("> ❌ Invalid Z coordinate");
    return false;
}

App.MainController?.AddLogMessage($"> ✅ Z = {ProbeHelper.FormatCoordinate(z)}");
```

## PropertyChangedManager Usage

### Basic Subscription
```csharp
private PropertyChangedManager _propManager = new PropertyChangedManager();

public MyViewModel()
{
    // Subscribe to all property changes
    _propManager.Subscribe(
        App.MainController,
        propertyName => OnControllerPropertyChanged(propertyName)
    );
}

private void OnControllerPropertyChanged(string propertyName)
{
    switch (propertyName)
    {
case nameof(MainControll.IsConnected):
            UpdateConnectionStatus();
          break;
        case nameof(MainControll.MachineStatus):
            UpdateMachineStatus();
            break;
    }
}

public void Dispose()
{
    _propManager?.Dispose(); // Auto-cleanup all subscriptions
}
```

### Subscribe to Specific Properties
```csharp
_propManager.SubscribeToProperties(
    App.MainController,
    new Dictionary<string, Action>
    {
        [nameof(MainControll.IsConnected)] = UpdateConnectionStatus,
        [nameof(MainControll.MachineStatus)] = UpdateMachineStatus,
      [nameof(MainControll.IsGCodeRunning)] = UpdateExecutionState
    }
);
```

### Subscribe with UI Thread Marshalling
```csharp
// Automatically dispatch to UI thread
_propManager.SubscribeWithUiDispatch(
    App.MainController,
    propertyName =>
    {
        // This code runs on UI thread
      StatusLabel.Text = $"Property changed: {propertyName}";
    }
);
```

### Scoped Subscription (Auto-dispose)
```csharp
public void StartMonitoring()
{
    var subscription = _propManager.CreateScopedSubscription(
 App.MainController,
        propertyName => Console.WriteLine($"Changed: {propertyName}")
    );
    
    // Store subscription, dispose when done:
    _currentSubscription = subscription;
}

public void StopMonitoring()
{
    _currentSubscription?.Dispose(); // Removes only this subscription
}
```

### Fluent Extension Methods
```csharp
// Subscribe to any property change:
var subscription = App.MainController.OnPropertyChanged(
    propertyName =>
    {
      if (propertyName == nameof(MainControll.IsConnected))
    {
            UpdateUI();
  }
    }
);

// Subscribe to specific property only:
var subscription = App.MainController.OnPropertyChanged(
    nameof(MainControll.IsConnected),
    () => UpdateConnectionStatus()
);

// Cleanup:
subscription.Dispose();
```

### Complex Subscription Example
```csharp
public class MyView : UserControl
{
    private PropertyChangedManager _propManager;
    private IDisposable _executionSubscription;
    
    public MyView()
    {
        InitializeComponent();
        _propManager = new PropertyChangedManager();
    
        // Always monitor connection
        _propManager.SubscribeWithUiDispatch(
       App.MainController,
          propertyName =>
  {
      if (propertyName == nameof(MainControll.IsConnected))
           {
             IsEnabled = App.MainController.IsConnected;
         }
       }
        );
        
        // Conditionally monitor execution
        StartExecutionMonitoring();
    }
    
    private void StartExecutionMonitoring()
 {
        _executionSubscription = App.MainController.OnPropertyChanged(
            nameof(MainControll.IsGCodeRunning),
            () => UpdateExecutionUI()
     );
    }
    
    private void StopExecutionMonitoring()
    {
  _executionSubscription?.Dispose();
        _executionSubscription = null;
    }
    
    private void UpdateExecutionUI()
    {
        RunButton.IsEnabled = !App.MainController.IsGCodeRunning;
   StopButton.IsEnabled = App.MainController.IsGCodeRunning;
    }
    
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _propManager?.Dispose(); // Cleanup all subscriptions
  _executionSubscription?.Dispose();
        base.OnUnloaded(e);
    }
}
```

## Common Patterns

### Pattern 1: Button with Safe Async Handler
```csharp
EventHandlerHelper.CreateButtonHandler(
    MyButton,
    async () => await PerformOperation(),
    "MyOperation",
    App.MainController?.AddLogMessage
);
```

### Pattern 2: Multi-Step Probe Operation
```csharp
var steps = new List<ProbeSequenceStep>
{
 new ProbeSequenceStep("G91", "Switch to relative", "G91") { WaitForIdle = false },
    new ProbeSequenceStep("G00 Z3.000", "Retract 3mm", "Retract"),
    new ProbeSequenceStep(
     ProbeHelper.BuildProbeCommand('Z', -30, coarseFeed),
        "Coarse probe",
        "CoarseProbe"
    ) { TimeoutMs = ProbeHelper.EstimateTimeoutForFeed(30, coarseFeed) }
};

bool success = await ProbeHelper.ExecuteProbeSequence(
    App.MainController.SendGCodeCommandWithConfirmationAsync,
    () => App.MainController?.MachineStatus,
    steps.ToArray(),
    App.MainController?.AddLogMessage
);
```

### Pattern 3: View with Managed Subscriptions
```csharp
public class MyView : UserControl, IDisposable
{
    private readonly PropertyChangedManager _subscriptions = new PropertyChangedManager();
    
    public MyView()
    {
  InitializeComponent();
        SetupSubscriptions();
    }
    
    private void SetupSubscriptions()
    {
        _subscriptions.SubscribeToProperties(App.MainController, new Dictionary<string, Action>
        {
     [nameof(MainControll.IsConnected)] = OnConnectionChanged,
   [nameof(MainControll.MachineStatus)] = OnStatusChanged
});
    }
    
    public void Dispose()
    {
        _subscriptions?.Dispose();
    }
}
```

## Migration Checklist

### For EventHandlerHelper:
- [ ] Identify repetitive event handlers
- [ ] Replace try-catch boilerplate with SafeHandleAsync/SafeHandle
- [ ] Use CreateJogButtonHandlers for similar button groups
- [ ] Add touch/mouse conflict filtering
- [ ] Test all input methods (mouse, touch, keyboard)

### For ProbeHelper:
- [ ] Replace manual idle waits with WaitForIdleAsync
- [ ] Use BuildProbeCommand/BuildRetractCommand for G-code
- [ ] Replace timeout calculations with EstimateTimeoutForFeed/ForRapid
- [ ] Use ValidateMeasurements for probe tolerance checking
- [ ] Consider ExecuteProbeSequence for multi-step operations

### For PropertyChangedManager:
- [ ] Identify manual PropertyChanged subscriptions
- [ ] Replace with SubscribeToProperties for clarity
- [ ] Use SubscribeWithUiDispatch for UI updates
- [ ] Verify Dispose() is called for cleanup
- [ ] Test for memory leaks with profiler

## Best Practices

### EventHandlerHelper:
- ✅ Always provide operation name for logging
- ✅ Use batch creation for similar handlers
- ✅ Filter touch-to-mouse promotion
- ❌ Don't suppress critical exceptions
- ❌ Don't forget to log errors

### ProbeHelper:
- ✅ Use consistent timeout calculations
- ✅ Validate coordinates with IsFinite
- ✅ Use BuildProbeCommand for consistency
- ❌ Don't hardcode feed rates
- ❌ Don't skip idle waits

### PropertyChangedManager:
- ✅ Always dispose to prevent leaks
- ✅ Use specific property subscriptions when possible
- ✅ Marshal to UI thread when updating UI
- ❌ Don't create circular dependencies
- ❌ Don't forget to unsubscribe in cleanup
