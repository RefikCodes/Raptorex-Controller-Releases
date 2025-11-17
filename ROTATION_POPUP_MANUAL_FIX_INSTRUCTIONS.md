## MANUAL FIX INSTRUCTIONS FOR RotationPopup.xaml.cs

### LOCATION: After line 81 in the constructor

**File:** `CncControlApp/Controls/RotationPopup.xaml.cs`

**Find these lines (around line 78-82):**
```csharp
public RotationPopup(CncControlApp.GCodeView gcodeView)
{
    InitializeComponent();
    _gcodeView = gcodeView;

    // Setup debounce timer for smooth rotation
```

**ADD these 4 lines between `_gcodeView = gcodeView;` and the `// Setup debounce timer` comment:**

```csharp
            // ✅ FIX: Explicitly initialize flag to ensure zero dialog shows on first G00 click
   _awaitingZeroPrompt = false;
            App.MainController?.AddLogMessage($"> RotationPopup: Zero prompt flag initialized to FALSE");

```

**RESULT should look like:**
```csharp
public RotationPopup(CncControlApp.GCodeView gcodeView)
{
    InitializeComponent();
    _gcodeView = gcodeView;

    // ✅ FIX: Explicitly initialize flag to ensure zero dialog shows on first G00 click
    _awaitingZeroPrompt = false;
    App.MainController?.AddLogMessage($"> RotationPopup: Zero prompt flag initialized to FALSE");

    // Setup debounce timer for smooth rotation
    _redrawDebounceTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(150) // Redraw after 150ms of no slider movement
    };
```

### WHY THIS FIX WORKS:

1. The `ApplyButton_Click` method sets `_awaitingZeroPrompt = false` on line 232, which is why the dialog works after clicking Apply once
2. By adding the same initialization in the constructor, the dialog will work on the FIRST G00 click
3. The log message will confirm the flag is being initialized

### AFTER APPLYING THE FIX:

1. Save the file
2. Rebuild the solution (Ctrl+Shift+B)
3. Run the application
4. When you open RotationPopup, check the log for: `> RotationPopup: Zero prompt flag initialized to FALSE`
5. Click canvas → select position → click G00 → **zero dialog should show immediately**

### VERIFICATION:

Look for these log messages:
```
> RotationPopup: Zero prompt flag initialized to FALSE
> RotationPopup: Fit to table ALWAYS enabled
> RotationPopup: Auto-zero ALWAYS enabled (no toggle)
> Rotation popup G53 jog: G53 G00 X123.456 Y78.901
> Post-G00: Checking zero prompt state - awaiting=False, cmdOk=True
> ✅ Zero prompt flag set - showing dialog
```

If you see `awaiting=True` instead of `awaiting=False`, the fix was not applied correctly.
