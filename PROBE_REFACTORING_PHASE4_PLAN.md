# Probe Operations Refactoring Plan - Phase 4

## Analysis Summary

### Duplicated Code Identified in Probe Operations:

#### 1. **Helper Methods (Already in ProbeHelper but duplicated)**
| Method | Current Location | Duplication Count | Lines |
|--------|-----------------|-------------------|-------|
| `WaitForIdleAsync()` | MainWindow.Probe.cs + MainWindow.ProbeHelpers.cs | 2 copies | ~30 lines each |
| `EstimateTimeoutMsForFeed()` | MainWindow.ProbeHelpers.cs | 1 copy | ~8 lines |
| `EstimateTimeoutMsForRapid()` | MainWindow.ProbeHelpers.cs | 1 copy | ~7 lines |
| `ClampProbeFeed()` | MainWindow.Probe.cs | 1 copy | ~5 lines |
| `GetAxisRapid()` | MainWindow.Probe.cs | 1 copy | ~12 lines |
| `AxisMove()` | MainWindow.Probe.cs | 1 copy | ~10 lines |

**Total Helper Duplication: ~72 lines**

#### 2. **Probe Sequence Patterns**
| Pattern | Occurrences | Lines Each | Total Lines |
|---------|-------------|------------|-------------|
| Retract → Idle Wait → Probe → Idle Wait | 6+ times | ~40 lines | ~240 lines |
| Edge Detection Loop (Z-drop) | 4 times (X left/right, Y left/right) | ~120 lines | ~480 lines |
| Axis Probe Call + Set Zero | 4 times | ~40 lines | ~160 lines |
| State Cleanup (Hold/Alarm) | 10+ times | ~15 lines | ~150 lines |

**Total Pattern Duplication: ~1,030 lines**

### Total Identified Duplication: ~1,100 lines

## Refactoring Strategy

### Phase 4A: Use Existing ProbeHelper Methods ✅
**Goal:** Replace duplicate helper methods with ProbeHelper calls

**Files to Modify:**
1. MainWindow.Probe.cs - Replace local helpers with ProbeHelper
2. MainWindow.ProbeHelpers.cs - Replace duplicate methods with ProbeHelper

**Expected Reduction:** ~72 lines

### Phase 4B: Extract Edge Detection Pattern 🎯
**Goal:** Create reusable edge detection sequence

**New Helper Method:**
```csharp
public static async Task<(bool success, double edgePosition)> DetectEdgeWithZDrop(
    Func<string, Task<bool>> sendCommand,
    Func<string> getStatus,
    Func<double> getWorkZ,
  char axis,
    int direction,
    int maxMoves = 10,
    double moveStep = 20.0,
    double zDropThreshold = -2.0,
    int zProbeFeed = 100)
```

**Expected Reduction:** ~480 lines

### Phase 4C: Consolidate Probe Sequences 🎯
**Goal:** Use ProbeHelper.ExecuteProbeSequence for common patterns

**Expected Reduction:** ~240 lines

### Phase 4D: Extract State Cleanup Pattern 🎯
**Goal:** Create reusable state cleanup method

**New Helper Method:**
```csharp
public static async Task<bool> EnsureIdleState(
  Func<Task<bool>> exitHold,
    Func<Task<bool>> clearAlarm,
 Func<string> getStatus,
    int timeoutMs = 2000)
```

**Expected Reduction:** ~150 lines

## Implementation Plan

### Quick Win: Phase 4A (Immediate, Low Risk)
Replace duplicate helper methods in MainWindow.Probe.cs and MainWindow.ProbeHelpers.cs:

**Before:**
```csharp
// MainWindow.Probe.cs
private async Task<bool> WaitForIdleAsync(int timeoutMs, string tag)
{
var sw = Stopwatch.StartNew();
    // ... 30 lines of duplicate code
}

private static int ClampProbeFeed(int feed)
{
    if (feed < 1) return 1;
  return feed > MaxProbeFeed ? MaxProbeFeed : feed;
}

private double GetAxisRapid(char axis)
{
    // ... 12 lines
}

// MainWindow.ProbeHelpers.cs
private int EstimateTimeoutMsForFeed(double distanceMm, int feedMmMin, int minMs =8000)
{
    // ... 8 lines
}

private int EstimateTimeoutMsForRapid(double distanceMm, double rapidMmMin, int minMs =5000)
{
// ... 7 lines
}
```

**After:**
```csharp
// Just use ProbeHelper static methods:
await ProbeHelper.WaitForIdleAsync(
    () => App.MainController?.MachineStatus,
  15000,
    "Coarse_Retract",
    App.MainController?.AddLogMessage);

int timeout = ProbeHelper.EstimateTimeoutForFeed(30.0, coarseFeed);
int feed = ProbeHelper.ClampFeed(rawFeed, 1, 1000);
```

**Implementation Steps:**
1. ✅ Add missing methods to ProbeHelper (GetAxisRapid, AxisMove)
2. ✅ Update MainWindow.Probe.cs to use ProbeHelper methods
3. ✅ Update MainWindow.ProbeHelpers.cs to use ProbeHelper methods
4. ✅ Remove duplicate method implementations
5. ✅ Test Z probe operation
6. ✅ Test X/Y probe operations

### Medium Effort: Phase 4B-4D (Strategic, Medium Risk)
Extract edge detection and state cleanup patterns:

**Expected Timeline:**
- Phase 4A: 1-2 hours (immediate value)
- Phase 4B: 3-4 hours (high value)
- Phase 4C: 2-3 hours (medium value)
- Phase 4D: 1-2 hours (low effort, good value)

**Total Effort: 7-11 hours**
**Total Reduction: ~940 lines** (85% of probe duplication)

## Risk Assessment

### Phase 4A: ✅ Low Risk
- Simple method replacements
- ProbeHelper methods already tested
- Easy to verify (compare behavior before/after)
- Can be rolled back easily

### Phase 4B-4D: ⚠️ Medium Risk
- More complex refactoring
- Touches critical probe sequences
- Requires extensive testing
- Benefits justify the risk

## Testing Strategy

### Phase 4A Testing:
- [ ] Z Probe: Complete sequence including coarse + fine measurements
- [ ] X+ Probe: Edge detection and positioning
- [ ] X- Probe: Edge detection and positioning
- [ ] Y+ Probe: Edge detection and positioning
- [ ] Y- Probe: Edge detection and positioning
- [ ] Center X Outer: Full sequence
- [ ] Center Y Outer: Full sequence
- [ ] Center XY Outer: Combined sequence

### Verification Checklist:
- [ ] Idle waits complete successfully
- [ ] Timeouts are appropriate for move distances
- [ ] Feed rates are clamped correctly
- [ ] Probe sequences execute without errors
- [ ] Position readings are accurate
- [ ] Error messages are logged properly

## Expected Benefits

### Code Reduction:
- **Phase 4A Only:** ~72 lines eliminated
- **Full Phases 4A-4D:** ~940 lines eliminated (85%)

### Maintainability:
- **Single source of truth** for probe helper methods
- **Reusable patterns** for edge detection
- **Consistent error handling** across all probe operations
- **Easier to add new probe types**

### Reliability:
- **Tested helper methods** reduce bugs
- **Consistent state management** prevents stuck states
- **Proper timeout calculations** prevent false failures

## Current Progress Tracking

| Phase | Status | Lines Eliminated | Time Spent |
|-------|--------|------------------|------------|
| Phase 1 | ✅ Complete | 415 lines | 8 hours |
| Phase 2 | ✅ Complete | 0 lines (helpers created) | 6 hours |
| Phase 3 | ✅ Complete | 346 lines (JogView) | 2 hours |
| **Phase 4A** | ⏸️ **READY** | ~72 lines (probe helpers) | **0 hours** |
| Phase 4B | 📋 Planned | ~480 lines (edge detection) | 0 hours |
| Phase 4C | 📋 Planned | ~240 lines (sequences) | 0 hours |
| Phase 4D | 📋 Planned | ~150 lines (state cleanup) | 0 hours |
| **TOTAL SO FAR** | - | **761 lines** | **16 hours** |
| **POTENTIAL TOTAL** | - | **~1,700 lines** | **~27 hours** |

## Recommendation

**START WITH PHASE 4A** - It's low-hanging fruit with immediate value:
- Only 1-2 hours of work
- ~72 lines eliminated
- Low risk (simple method replacements)
- Validates ProbeHelper in real probe operations
- Sets foundation for Phase 4B-4D if desired

Once Phase 4A is complete and tested, we can evaluate whether to proceed with Phase 4B-4D based on:
- Available time/resources
- Testing capacity
- Risk tolerance
- Actual need for additional refactoring

## Next Steps

1. **Immediate:** Implement Phase 4A (use ProbeHelper methods in probe operations)
2. **Test:** Verify all probe operations work correctly
3. **Evaluate:** Decide whether to proceed with Phase 4B-4D
4. **Document:** Update refactoring summary with results

---

**Ready to proceed with Phase 4A?** ✅
