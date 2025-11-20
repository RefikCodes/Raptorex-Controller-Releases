# GCodeVisualization.cs Fix Required

## Issue
The file now has:
1. Duplicate methods (old + new versions)
2. References to removed `_unifiedScale` field
3. Old `RenderTopViewSimple` method still exists alongside new one

## Required Manual Fixes

### Step 1: Remove OLD duplicate methods (keep the OPTIMIZED versions)

**Remove these OLD methods entirely:**
- `GetUnifiedScaleFromSettings(double canvasWidth, double canvasHeight)` - appears twice, remove old one
- `DrawGCodeCentered(...)` - appears 3 times, keep only the NEW `DrawGCodeWithTransform`
- `GetTableDimensionsFromSettings()` - appears twice, keep only one
- `CalculateGCodeBounds(...)` - appears twice, keep only one
- `GetOverlayManager()` - appears twice, keep only one
- `GetCurrentFitState()` - appears twice, keep only one

### Step 2: Remove `_unifiedScale` field references

**Find and replace all instances of `_unifiedScale` with the WorkspaceTransform approach:**

OLD code (around line 1471-1501):
```csharp
bool settingsAvailable = GetUnifiedScaleFromSettings(canvasWidth, canvasHeight);

if (!settingsAvailable)
{
    DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
    System.Diagnostics.Debug.WriteLine($"❌ Cannot render: No valid settings");
    return null;
}

System.Diagnostics.Debug.WriteLine($"🔄 Using settings-based scale: {_unifiedScale:F3}");

var overlayManager = GetOverlayManager();
if (overlayManager != null)
{
    overlayManager.UpdateDynamicScale(_unifiedScale, canvasWidth, canvasHeight);
    System.Diagnostics.Debug.WriteLine($"✅ Updated overlay manager with settings scale: {_unifiedScale:F3}");
}

Point? originPosition = null;
if (gcodeSegments?.Count > 0)
{
    originPosition = DrawGCodeCentered(mainCanvas, gcodeSegments, _unifiedScale, canvasWidth, canvasHeight);
}

RefreshTopViewOverlay();

System.Diagnostics.Debug.WriteLine($"✅ SIMPLE RENDER complete (Settings Scale={_unifiedScale:F3})");
return originPosition;
```

SHOULD BE (the NEW optimized version at the top):
```csharp
// ✅ STEP 1: Create WorkspaceTransform ONCE (eliminates 66% redundant calculations)
if (!WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
{
    DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
    System.Diagnostics.Debug.WriteLine($"❌ Cannot render: No valid settings ($130/$131)");
    return null;
}

System.Diagnostics.Debug.WriteLine($"✅ WorkspaceTransform created: Scale={xf.Scale:F3}, Table={xf.MaxX:F0}x{xf.MaxY:F0}mm");

// ✅ STEP 2: Update overlay with the SAME transform (no recalculation)
var overlayManager = GetOverlayManager();
if (overlayManager != null)
{
    overlayManager.UpdateWithTransform(xf);
    System.Diagnostics.Debug.WriteLine($"✅ Overlay updated with unified transform");
}

// ✅ STEP 3: Draw G-CODE using the SAME transform (no recalculation)
Point? originPosition = null;
if (gcodeSegments?.Count > 0)
{
    originPosition = DrawGCodeWithTransform(mainCanvas, gcodeSegments, xf);
}

// ✅ STEP 4: Refresh overlay
RefreshTopViewOverlay();

System.Diagnostics.Debug.WriteLine($"✅ OPTIMIZED RENDER complete (Scale={xf.Scale:F3})");
return originPosition;
```

### Step 3: Ensure only ONE version of each method exists

**Keep these OPTIMIZED methods:**
1. `RenderTopViewSimple` (new optimized version that creates WorkspaceTransform once)
2. `DrawGCodeWithTransform` (renamed from DrawGCodeCentered, takes WorkspaceTransform)
3. `DrawOriginMarker` (unchanged)
4. Single version of helper methods (GetTableDimensionsFromSettings, CalculateGCodeBounds, etc.)

**Remove these methods:**
1. Old `GetUnifiedScaleFromSettings` method
2. Old `DrawGCodeCentered` methods (all 3 copies)
3. `_unifiedScale` field declaration at top of class

### Step 4: Fix remaining OLD RenderTopViewSimple

The file likely has TWO RenderTopViewSimple methods - remove the old one that uses `GetUnifiedScaleFromSettings` and `_unifiedScale`.

## Quick Fix Command

**Search for:** `private double _unifiedScale`
**Delete:** The entire line

**Search for:** `private bool GetUnifiedScaleFromSettings(`
**Delete:** The entire method (from `private bool GetUnifiedScaleFromSettings` to its closing `}`)

**Search for:** `private Point? DrawGCodeCentered(Canvas canvas, List<GCodeSegment> gcodeSegments, double scale`
**Count:** Should find 3 occurrences
**Action:** Delete all 3 (we renamed it to `DrawGCodeWithTransform`)

**Search for the OLD RenderTopViewSimple** that starts with:
```csharp
bool settingsAvailable = GetUnifiedScaleFromSettings(canvasWidth, canvasHeight);
```
**Replace entire method** with the new version that uses `WorkspaceTransform.TryCreateFromSettings`.

## Expected Result

After fixes:
- ✅ Only ONE `RenderTopViewSimple` (optimized version)
- ✅ Only ONE `DrawGCodeWithTransform` (renamed from DrawGCodeCentered)
- ✅ NO `_unifiedScale` field
- ✅ NO `GetUnifiedScaleFromSettings` method
- ✅ NO duplicate methods
- ✅ Overlay manager uses `UpdateWithTransform(xf)` instead of `UpdateDynamicScale`

## Verification

After fixes, search for:
- `_unifiedScale` → Should find ZERO results
- `GetUnifiedScaleFromSettings` → Should find ZERO results  
- `DrawGCodeCentered` → Should find ZERO results
- `DrawGCodeWithTransform` → Should find EXACTLY ONE definition and multiple calls to it
- `WorkspaceTransform.TryCreateFromSettings` → Should find calls in RenderTopViewSimple

Build should succeed with no duplicate method errors.
