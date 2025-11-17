using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IoPath = System.IO.Path;

namespace CncControlApp
{
    /// <summary>
    /// Comprehensive G-Code file service - Handles file operations, UI updates, and execution integration
    /// </summary>
    public class GCodeFileService
    {
        #region Fields

        private readonly GCodeFileManager _fileManager;
        private readonly GCodeParser _parser;
        private readonly ViewportManager _viewportManager;
        private readonly GCodeVisualization _visualization;
        
        // Collections
        private readonly ObservableCollection<GCodeLineItem> _gcodeLines;
        private readonly List<GCodeSegment> _gcodeSegments;

        #endregion

        #region Events

        /// <summary>
        /// Fired when file information is updated (fileName, fileSize, lineCount)
        /// </summary>
        public event Action<string, long, int> FileInformationUpdated;

        /// <summary>
        /// Fired when distance statistics are calculated (xRange, yRange, zRange, xTotal, yTotal, zTotal)
        /// </summary>
        public event Action<double, double, double, double, double, double> DistanceStatisticsUpdated;

        /// <summary>
        /// 🆕 Fired when cutting distance statistics are calculated (linearDistance, rapidDistance, arcDistance, totalDistance)
        /// </summary>
        public event Action<double, double, double, double> CuttingDistanceStatisticsUpdated;

        /// <summary>
        /// Fired when file loading is completed successfully
        /// </summary>
        public event Action<string> FileLoadCompleted;

        /// <summary>
        /// Fired when an error occurs during file operations
        /// </summary>
        public event Action<string, string> ErrorOccurred;

        /// <summary>
        /// Fired when loading progress changes (percentage, statusMessage)
        /// </summary>
        public event Action<double, string> LoadingProgressChanged;

        /// <summary>
        /// 🆕 Fired when cutting time statistics are calculated (linearTime, rapidTime, arcTime, totalTime in minutes)
        /// </summary>
        public event Action<double, double, double, double> CuttingTimeStatisticsUpdated;

        #endregion

        #region Properties

        /// <summary>
        /// Currently loaded G-Code lines for UI display
        /// </summary>
        public ObservableCollection<GCodeLineItem> GCodeLines => _gcodeLines;

        /// <summary>
        /// Currently loaded G-Code segments for rendering and analysis
        /// </summary>
        public List<GCodeSegment> GCodeSegments => _gcodeSegments;

        /// <summary>
        /// Get the visualization instance for advanced rendering (popup windows, etc.)
        /// </summary>
        public GCodeVisualization GetVisualization() => _visualization;

        /// <summary>
        /// Path of the currently loaded file
        /// </summary>
        public string CurrentFilePath { get; private set; }

        /// <summary>
        /// Name of the currently loaded file
        /// </summary>
        public string CurrentFileName { get; private set; }

        /// <summary>
        /// Indicates if a G-Code file is currently loaded
        /// </summary>
        public bool IsFileLoaded => !string.IsNullOrEmpty(CurrentFilePath) && _gcodeLines.Count >0;

        #endregion

        #region Constructor

        public GCodeFileService(ViewportManager viewportManager)
        {
            _fileManager = new GCodeFileManager();
            _parser = new GCodeParser();
            _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
            _visualization = new GCodeVisualization(_viewportManager);

            _gcodeLines = new ObservableCollection<GCodeLineItem>();
            _gcodeSegments = new List<GCodeSegment>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Open file dialog and load selected G-Code file
        /// </summary>
        /// <param name="owner">Owner window for the dialog</param>
        /// <returns>True if file was loaded successfully</returns>
        public async Task<bool> OpenAndLoadFileAsync(Window owner)
        {
            try
            {
                // Show custom file dialog
                var customDialog = new CustomFileDialog()
                {
                    Owner = owner
                };

                customDialog.ShowDialog();

                if (customDialog.DialogResult && !string.IsNullOrEmpty(customDialog.SelectedFile))
                {
                    string filePath = customDialog.SelectedFile;
                    return await LoadFileWithProgressAsync(filePath);
                }

                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("File dialog error", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Load G-Code file with progress dialog
        /// </summary>
        /// <param name="filePath">Path to the G-Code file</param>
        /// <returns>True if file was loaded successfully</returns>
        public async Task<bool> LoadFileWithProgressAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ErrorOccurred?.Invoke("File not found", $"The file '{filePath}' does not exist.");
                return false;
            }

            try
            {
                string fileName = IoPath.GetFileName(filePath);
                
                // Create and show progress dialog
                var progressDialog = new GCodeProgressDialog();
                progressDialog.Show();

                bool success = await ProcessFileWithProgressAsync(filePath, fileName, progressDialog);

                progressDialog.CloseDialog();

                if (success)
                {
                    CurrentFilePath = filePath;
                    CurrentFileName = fileName;
                    FileLoadCompleted?.Invoke(fileName);

                    // Load file into MainController for execution
                    bool executionLoadSuccess = App.MainController?.LoadGCodeFile(filePath) ?? false;
                    if (!executionLoadSuccess)
                    {
                        App.MainController?.AddLogMessage($"> ⚠️ File loaded for viewing but not for execution: {fileName}");
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("File loading error", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Previously loaded segments ile tüm viewport'ları yeniden çizer.
        /// Panel/sekme geri geldiğinde veya görünürlük değiştiğinde çağırın.
        /// ✅ OPTIMIZED: Immediate rendering for better responsiveness
        /// </summary>
        public void RedrawAllViewports()
        {
            try
            {
                if (_gcodeSegments == null || _gcodeSegments.Count ==0)
                {
                    System.Diagnostics.Debug.WriteLine("> 🔄 Redraw skipped: No segments loaded");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"> 🎨 Redrawing all viewports: {_gcodeSegments.Count} segments");

                // ✅ OPTIMIZED: Render immediately on current thread for faster response
                // Only use background priority for last viewport to prevent UI blocking
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Top, _gcodeSegments);
                }, System.Windows.Threading.DispatcherPriority.Send);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Right, _gcodeSegments);
                }, System.Windows.Threading.DispatcherPriority.Send);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Front, _gcodeSegments);
                }, System.Windows.Threading.DispatcherPriority.Send);

                // ✅ Last viewport can use lower priority
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Isometric, _gcodeSegments);
                }), System.Windows.Threading.DispatcherPriority.Render);

                System.Diagnostics.Debug.WriteLine("> ✅ All viewports redrawn successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RedrawAllViewports error: {ex.Message}");
                ErrorOccurred?.Invoke("Redraw error", ex.Message);
            }
        }

        /// <summary>
        /// 🆕 Re-parses the current DisplayGCodeLines into fresh segments and redraws all viewports.
        /// Use after rotation or any transformation that modifies the G-code text lines.
        /// This ensures canvases render from the updated G-code, not stale cached segments.
        /// </summary>
        public void ReparseAndRedrawFromLines()
        {
            try
            {
                if (_gcodeLines == null || _gcodeLines.Count ==0)
                {
                    App.MainController?.AddLogMessage("> ⚠️ Reparse skipped: No G-code lines loaded");
                    return;
                }

                App.MainController?.AddLogMessage("> 🔄 Reparsing G-code lines for canvas update...");

                // Clear existing segments
                _gcodeSegments.Clear();

                // Reset parser state for fresh parse
                _parser.Reset();

                // Re-parse all lines into segments
                foreach (var lineItem in _gcodeLines)
                {
                    if (!string.IsNullOrWhiteSpace(lineItem.GCodeLine))
                    {
                        _parser.ParseGCodeLine(lineItem.GCodeLine, lineItem.LineNumber, _gcodeSegments);
                    }
                }

                App.MainController?.AddLogMessage($"> ✅ Reparsed {_gcodeSegments.Count} segments from {_gcodeLines.Count} lines");

                // NEW: Provide segments to execution manager for time/remaining estimates and Z-layer tracking
                try
                {
                    var mgr = App.MainController?.GCodeManager;
                    mgr?.CalculateEstimatedExecutionTime(_gcodeSegments);
                    mgr?.CacheUniqueZLayers(_gcodeSegments);
                }
                catch { }

                // Update statistics from new segments
                var distanceStats = CalculateDistanceStatistics();
                DistanceStatisticsUpdated?.Invoke(
                    distanceStats.XRange, distanceStats.YRange, distanceStats.ZRange,
                    distanceStats.XTotalDistance, distanceStats.YTotalDistance, distanceStats.ZTotalDistance);

                var cuttingStats = CalculateCuttingDistanceStatistics();
                CuttingDistanceStatisticsUpdated?.Invoke(
                    cuttingStats.LinearDistance, cuttingStats.RapidDistance,
                    cuttingStats.ArcDistance, cuttingStats.TotalDistance);

                var timeStats = CalculateAccurateCuttingTimeStatistics();
                CuttingTimeStatisticsUpdated?.Invoke(
                    timeStats.LinearTime, timeStats.RapidTime, 
                    timeStats.ArcTime, timeStats.TotalTime);

                // Redraw all viewports with new segments
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Top, _gcodeSegments);
                    _visualization.RenderCanvas(ViewportType.Right, _gcodeSegments);
                    _visualization.RenderCanvas(ViewportType.Front, _gcodeSegments);
                    _visualization.RenderCanvas(ViewportType.Isometric, _gcodeSegments);
                }, System.Windows.Threading.DispatcherPriority.Send);

                App.MainController?.AddLogMessage("> 🎨 All viewports redrawn from reparsed segments");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Reparse error", ex.Message);
                App.MainController?.AddLogMessage($"> ❌ Reparse failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 🆕 Reprocess current G-code lines as if opening a new file (for rotation, transformations, etc.)
        /// If showProgressDialog is true, shows a progress dialog. Otherwise, streams progress via provided delegate.
        /// </summary>
        public async Task ReprocessCurrentLinesAsNewFileAsync(Action<string> progress = null, bool showProgressDialog = true)
        {
            try
            {
                if (_gcodeLines == null || _gcodeLines.Count ==0)
                {
                    App.MainController?.AddLogMessage("> ⚠️ Reprocess skipped: No G-code lines loaded");
                    return;
                }

                App.MainController?.AddLogMessage("> 🔄 Reprocessing G-code as new file...");

                GCodeProgressDialog progressDialog = null;
                if (showProgressDialog)
                {
                    progressDialog = new GCodeProgressDialog();
                    progressDialog.Show();
                }

                Action<string> report = msg =>
                {
                    try
                    {
                        if (string.IsNullOrEmpty(msg)) return;
                        if (showProgressDialog && progressDialog != null) progressDialog.UpdateStatus(msg);
                        progress?.Invoke(msg);
                    }
                    catch { }
                };

                Action<int> setPct = pct =>
                {
                    try
                    {
                        if (showProgressDialog && progressDialog != null) progressDialog.SetProgress(pct);
                    }
                    catch { }
                };

                await Task.Run(async () =>
                {
                    try
                    {
                        // Step1: Clear and reparse (20% progress)
                        report("Parsing G-Code...");
                        _gcodeSegments.Clear();
                        _parser.Reset();

                        foreach (var lineItem in _gcodeLines)
                        {
                            if (!string.IsNullOrWhiteSpace(lineItem.GCodeLine))
                            {
                                _parser.ParseGCodeLine(lineItem.GCodeLine, lineItem.LineNumber, _gcodeSegments);
                            }
                        }

                        // NEW: update execution manager with fresh segments
                        try
                        {
                            var mgr = App.MainController?.GCodeManager;
                            mgr?.CalculateEstimatedExecutionTime(_gcodeSegments);
                            mgr?.CacheUniqueZLayers(_gcodeSegments);
                        }
                        catch { }

                        setPct(20);
                        await Task.Delay(50);

                        // Step2: File info (10% progress)
                        report("Calculating file information...");
                        FileInformationUpdated?.Invoke(CurrentFileName ?? "Rotated G-Code",0, _gcodeLines.Count);
                        setPct(30);
                        await Task.Delay(50);

                        // Step3: Distance stats (10% progress)
                        report("Analyzing G-Code dimensions...");
                        var distanceStats = CalculateDistanceStatistics();
                        DistanceStatisticsUpdated?.Invoke(
                            distanceStats.XRange, distanceStats.YRange, distanceStats.ZRange,
                            distanceStats.XTotalDistance, distanceStats.YTotalDistance, distanceStats.ZTotalDistance);
                        setPct(40);
                        await Task.Delay(50);

                        // Step3.5: Cutting distance (5% progress)
                        report("Calculating cutting distances...");
                        var cuttingStats = CalculateCuttingDistanceStatistics();
                        CuttingDistanceStatisticsUpdated?.Invoke(
                            cuttingStats.LinearDistance, cuttingStats.RapidDistance,
                            cuttingStats.ArcDistance, cuttingStats.TotalDistance);
                        setPct(45);
                        await Task.Delay(50);

                        // Step3.6: Cutting time (5% progress)
                        report("Calculating cutting times...");
                        var timeStats = CalculateAccurateCuttingTimeStatistics();
                        CuttingTimeStatisticsUpdated?.Invoke(
                            timeStats.LinearTime, timeStats.RapidTime,
                            timeStats.ArcTime, timeStats.TotalTime);
                        setPct(50);
                        await Task.Delay(50);

                        // Step4-7: Render all viewports synchronously (50% progress total)
                        if (showProgressDialog && progressDialog != null)
                        {
                            await RenderViewportWithProgress(ViewportType.Top, progressDialog,60, "Rendering Top View...");
                            await RenderViewportWithProgress(ViewportType.Right, progressDialog,75, "Rendering Right View...");
                            await RenderViewportWithProgress(ViewportType.Front, progressDialog,90, "Rendering Front View...");
                            await RenderViewportWithProgress(ViewportType.Isometric, progressDialog,100, "Rendering Isometric View...");
                        }
                        else
                        {
                            report("Rendering Top View...");
                            Application.Current.Dispatcher.Invoke(() => _visualization.RenderCanvas(ViewportType.Top, _gcodeSegments), System.Windows.Threading.DispatcherPriority.Send);
                            setPct(60);
                            report("Rendering Right View...");
                            Application.Current.Dispatcher.Invoke(() => _visualization.RenderCanvas(ViewportType.Right, _gcodeSegments), System.Windows.Threading.DispatcherPriority.Send);
                            setPct(75);
                            report("Rendering Front View...");
                            Application.Current.Dispatcher.Invoke(() => _visualization.RenderCanvas(ViewportType.Front, _gcodeSegments), System.Windows.Threading.DispatcherPriority.Send);
                            setPct(90);
                            report("Rendering Isometric View...");
                            Application.Current.Dispatcher.Invoke(() => _visualization.RenderCanvas(ViewportType.Isometric, _gcodeSegments), System.Windows.Threading.DispatcherPriority.Send);
                            setPct(100);
                        }

                        // Final sync: Ensure ALL rendering complete before closing
                        report("Finalizing canvas rendering...");
                        await Task.Run(() =>
                        {
                            Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                            Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                        });
                        await Task.Delay(200);

                        if (showProgressDialog && progressDialog != null)
                        {
                            progressDialog.ShowCompletionAndAutoClose("G-Code reprocessed successfully!",800);
                        }
                        App.MainController?.AddLogMessage($"> ✅ Reprocessed {_gcodeSegments.Count} segments from {_gcodeLines.Count} lines");
                        report("Reprocess completed");
                    }
                    catch (Exception ex)
                    {
                        report($"Error: {ex.Message}");
                        ErrorOccurred?.Invoke("Reprocess error", ex.Message);
                        App.MainController?.AddLogMessage($"> ❌ Reprocess failed: {ex.Message}");
                    }
                });

                if (showProgressDialog && progressDialog != null)
                {
                    progressDialog.CloseDialog();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Reprocess error", ex.Message);
                App.MainController?.AddLogMessage($"> ❌ Reprocess failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-render only the Top viewport (used on maximize / resize of Top view).
        /// Safe no-op if no segments loaded.
        /// </summary>
        public void RedrawTopViewport()
        {
            try
            {
                if (_gcodeSegments == null || _gcodeSegments.Count ==0) return;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _visualization.RenderCanvas(ViewportType.Top, _gcodeSegments);
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Redraw top view error", ex.Message);
            }
        }

        /// <summary>
        /// Clear all loaded G-Code data
        /// </summary>
        public void ClearAllData()
        {
            try
            {
                // Clear collections
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _gcodeLines.Clear();
                    _gcodeSegments.Clear();
                });

                // Clear viewports
                _viewportManager?.ClearAllViewports();

                // Reset properties
                CurrentFilePath = null;
                CurrentFileName = null;

                // Notify UI to reset
                FileInformationUpdated?.Invoke("No file selected",0,0);
                DistanceStatisticsUpdated?.Invoke(0,0,0,0,0,0);
                CuttingDistanceStatisticsUpdated?.Invoke(0,0,0,0);
                CuttingTimeStatisticsUpdated?.Invoke(0,0,0,0);

                App.MainController?.AddLogMessage("> 🗑️ All G-Code data cleared successfully");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Clear data error", ex.Message);
            }
        }

        /// <summary>
        /// Get file statistics for currently loaded file
        /// </summary>
        /// <returns>File statistics or null if no file is loaded</returns>
        public FileStatistics GetFileStatistics()
        {
            if (!IsFileLoaded)
                return null;

            try
            {
                var fileInfo = new FileInfo(CurrentFilePath);
                var distanceStats = CalculateDistanceStatistics();
                var cuttingStats = CalculateCuttingDistanceStatistics();

                return new FileStatistics
                {
                    FileName = CurrentFileName,
                    FilePath = CurrentFilePath,
                    FileSize = fileInfo.Length,
                    LineCount = _gcodeLines.Count,
                    SegmentCount = _gcodeSegments.Count,
                    XRange = distanceStats.XRange,
                    YRange = distanceStats.YRange,
                    ZRange = distanceStats.ZRange,
                    XTotalDistance = distanceStats.XTotalDistance,
                    YTotalDistance = distanceStats.YTotalDistance,
                    ZTotalDistance = distanceStats.ZTotalDistance,
                    // Add cutting distance statistics
                    LinearCuttingDistance = cuttingStats.LinearDistance,
                    RapidMovementDistance = cuttingStats.RapidDistance,
                    ArcCuttingDistance = cuttingStats.ArcDistance,
                    TotalCuttingDistance = cuttingStats.TotalDistance
                };
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Statistics calculation error", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Notify listeners that file info (especially line count) changed due to in-memory transformations.
        /// Also triggers execution time re-estimation so RemainingTime updates.
        /// </summary>
        public void NotifyFileInfoChangedForCurrentLines(string reason = null)
        {
            try
            {
                var name = CurrentFileName ?? "Rotated G-Code";
                FileInformationUpdated?.Invoke(name,0, _gcodeLines?.Count ??0);
                // Recalculate estimated execution time if segments available
                if (_gcodeSegments != null && _gcodeSegments.Count >0)
                {
                    try { App.MainController?.CalculateEstimatedExecutionTime(_gcodeSegments); } catch { }
                }
                if (!string.IsNullOrEmpty(reason))
                {
                    App.MainController?.AddLogMessage($"> ℹ️ File info updated ({reason}): lines={_gcodeLines?.Count ??0}");
                }
            }
            catch { }
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Process file with progress updates
        /// </summary>
        private async Task<bool> ProcessFileWithProgressAsync(string filePath, string fileName, GCodeProgressDialog progressDialog)
        {
            try
            {
                // Step1: Load file (20% progress)
                progressDialog.UpdateStatus("Loading G-Code file...");
                progressDialog.SetProgress(10);
                LoadingProgressChanged?.Invoke(10, "Loading G-Code file...");

                _fileManager.LoadGCodeFile(filePath, _gcodeLines, _gcodeSegments, _parser);
                
                progressDialog.SetProgress(20);
                LoadingProgressChanged?.Invoke(20, "File loaded successfully");

                await Task.Delay(100);

                // Step2: Update file information (10% progress)
                progressDialog.UpdateStatus("Calculating file statistics...");
                var fileInfo = new FileInfo(filePath);
                FileInformationUpdated?.Invoke(fileName, fileInfo.Length, _gcodeLines.Count);
                
                progressDialog.SetProgress(30);
                LoadingProgressChanged?.Invoke(30, "File statistics calculated");

                await Task.Delay(100);

                // Step3: Calculate distance statistics (10% progress)
                progressDialog.UpdateStatus("Analyzing G-Code dimensions...");
                var distanceStats = CalculateDistanceStatistics();
                DistanceStatisticsUpdated?.Invoke(
                    distanceStats.XRange, distanceStats.YRange, distanceStats.ZRange,
                    distanceStats.XTotalDistance, distanceStats.YTotalDistance, distanceStats.ZTotalDistance);

                progressDialog.SetProgress(40);
                LoadingProgressChanged?.Invoke(40, "Dimensions analyzed");

                await Task.Delay(100);

                // Step3.5: Calculate cutting distance statistics (5% progress)
                progressDialog.UpdateStatus("Calculating cutting distances...");
                var cuttingStats = CalculateCuttingDistanceStatistics();
                CuttingDistanceStatisticsUpdated?.Invoke(
                    cuttingStats.LinearDistance, cuttingStats.RapidDistance,
                    cuttingStats.ArcDistance, cuttingStats.TotalDistance);

                progressDialog.SetProgress(45);
                LoadingProgressChanged?.Invoke(45, "Cutting distances calculated");

                await Task.Delay(100);

                // Step3.6: Calculate cutting time statistics (5% progress)
                progressDialog.UpdateStatus("Calculating cutting times...");
                var timeStats = CalculateAccurateCuttingTimeStatistics();
                CuttingTimeStatisticsUpdated?.Invoke(
                    timeStats.LinearTime, timeStats.RapidTime,
                    timeStats.ArcTime, timeStats.TotalTime);

                progressDialog.SetProgress(50);
                LoadingProgressChanged?.Invoke(50, "Cutting times calculated");

                await Task.Delay(100);

                // NEW: Provide segments to execution manager for estimated runtime map and Z-layer tracking
                try
                {
                    var mgr = App.MainController?.GCodeManager;
                    mgr?.CalculateEstimatedExecutionTime(_gcodeSegments);
                    mgr?.CacheUniqueZLayers(_gcodeSegments);
                }
                catch { }

                // Step4-7: Render viewports (15% each =55% total)
                await RenderViewportWithProgress(ViewportType.Top, progressDialog,60, "Rendering Top View...");
                await RenderViewportWithProgress(ViewportType.Right, progressDialog,75, "Rendering Right View...");
                await RenderViewportWithProgress(ViewportType.Front, progressDialog,90, "Rendering Front View...");
                await RenderViewportWithProgress(ViewportType.Isometric, progressDialog,100, "Rendering Isometric View...");

                // 🆕 CRITICAL: Ensure ALL canvas rendering is fully completed before closing popup
                // This eliminates lag when opening multi-view later
                progressDialog.UpdateStatus("Finalizing canvas rendering...");
                await Task.Run(() =>
                {
                    // Force synchronous completion of all pending UI operations at Render priority
                    Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                });

                await Task.Delay(200); // Extra buffer to ensure all GPU operations complete

                progressDialog.ShowCompletionAndAutoClose("File processing completed successfully!",800);
                LoadingProgressChanged?.Invoke(100, "All operations completed");

                return true;
            }
            catch (Exception ex)
            {
                progressDialog.UpdateStatus($"Error: {ex.Message}");
                ErrorOccurred?.Invoke("File processing error", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Render single viewport with progress update - ensures complete synchronous rendering
        /// </summary>
        private async Task RenderViewportWithProgress(ViewportType viewportType, GCodeProgressDialog progressDialog, 
            double progressPercentage, string statusMessage)
        {
            progressDialog.UpdateStatus(statusMessage);
            LoadingProgressChanged?.Invoke(progressPercentage -10, statusMessage);

            // 🆕 Use synchronous Dispatcher.Invoke to ensure rendering completes before continuing
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _visualization.RenderCanvas(viewportType, _gcodeSegments);
                }, System.Windows.Threading.DispatcherPriority.Send); // Highest priority for immediate execution
            });

            progressDialog.SetProgress(progressPercentage);
            LoadingProgressChanged?.Invoke(progressPercentage, $"{statusMessage} - Completed");

            await Task.Delay(50); // Small delay to allow UI to update progress
        }

        /// <summary>
        /// Calculate distance statistics for loaded G-Code
        /// </summary>
        private (double XRange, double YRange, double ZRange, double XTotalDistance, double YTotalDistance, double ZTotalDistance) CalculateDistanceStatistics()
        {
            try
            {
                if (_gcodeSegments == null || _gcodeSegments.Count ==0)
                    return (0,0,0,0,0,0);

                // Calculate Min/Max values
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                double minZ = double.MaxValue, maxZ = double.MinValue;

                // Calculate total movement distances
                double totalXDistance =0;
                double totalYDistance =0;
                double totalZDistance =0;

                foreach (var segment in _gcodeSegments)
                {
                    // Update Min/Max values
                    minX = Math.Min(minX, Math.Min(segment.StartPoint.X, segment.EndPoint.X));
                    maxX = Math.Max(maxX, Math.Max(segment.StartPoint.X, segment.EndPoint.X));

                    minY = Math.Min(minY, Math.Min(segment.StartPoint.Y, segment.EndPoint.Y));
                    maxY = Math.Max(maxY, Math.Max(segment.StartPoint.Y, segment.EndPoint.Y));

                    minZ = Math.Min(minZ, Math.Min(segment.StartPoint.Z, segment.EndPoint.Z));
                    maxZ = Math.Max(maxZ, Math.Max(segment.StartPoint.Z, segment.EndPoint.Z));

                    // Calculate distance only for actual movement segments
                    if (segment.MovementType == GCodeMovementType.Linear ||
                        segment.MovementType == GCodeMovementType.Rapid ||
                        segment.MovementType == GCodeMovementType.Arc)
                    {
                        double deltaX = Math.Abs(segment.EndPoint.X - segment.StartPoint.X);
                        double deltaY = Math.Abs(segment.EndPoint.Y - segment.StartPoint.Y);
                        double deltaZ = Math.Abs(segment.EndPoint.Z - segment.StartPoint.Z);

                        totalXDistance += deltaX;
                        totalYDistance += deltaY;
                        totalZDistance += deltaZ;
                    }
                }

                // Range calculation (workspace dimensions)
                double xRange = maxX == double.MinValue ?0 : maxX - minX;
                double yRange = maxY == double.MinValue ?0 : maxY - minY;
                double zRange = maxZ == double.MinValue ?0 : maxZ - minZ;

                return (xRange, yRange, zRange, totalXDistance, totalYDistance, totalZDistance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CalculateDistanceStatistics error: {ex.Message}");
                return (0,0,0,0,0,0);
            }
        }

        /// <summary>
        /// Calculate cutting distance statistics by movement type using accurate arc calculations
        /// </summary>
        private (double LinearDistance, double RapidDistance, double ArcDistance, double TotalDistance) CalculateCuttingDistanceStatistics()
        {
            try
            {
                if (_gcodeSegments == null || _gcodeSegments.Count ==0)
                    return (0,0,0,0);

                double linearDistance =0;
                double rapidDistance =0;
                double arcDistance =0;

                // 🆕 Use the parser for accurate distance calculations
                var parser = new GCodeParser();

                foreach (var segment in _gcodeSegments)
                {
                    // 🆕 Calculate accurate distance using parser
                    double distance = parser.CalculateSegmentDistance(segment);
                    
                    // Cache the distance for performance
                    segment.SetCalculatedDistance(distance);

                    switch (segment.MovementType)
                    {
                        case GCodeMovementType.Linear:
                            linearDistance += distance;
                            break;
                        case GCodeMovementType.Rapid:
                            rapidDistance += distance;
                            break;
                        case GCodeMovementType.Arc:
                            arcDistance += distance;
                            break;
                    }
                }

                double totalDistance = linearDistance + rapidDistance + arcDistance;

                System.Diagnostics.Debug.WriteLine($"📊 DISTANCE CALCULATION SUMMARY:");
                System.Diagnostics.Debug.WriteLine($"📊 Linear distance: {linearDistance:F1}mm");
                System.Diagnostics.Debug.WriteLine($"📊 Rapid distance: {rapidDistance:F1}mm");
                System.Diagnostics.Debug.WriteLine($"📊 Arc distance: {arcDistance:F1}mm");
                System.Diagnostics.Debug.WriteLine($"📊 Total distance: {totalDistance:F1}mm");

                return (linearDistance, rapidDistance, arcDistance, totalDistance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CalculateCuttingDistanceStatistics error: {ex.Message}");
                return (0,0,0,0);
            }
        }

        /// <summary>
        /// Calculate accurate cutting time statistics using modal feed rates from segments
        /// </summary>
        private (double LinearTime, double RapidTime, double ArcTime, double TotalTime) CalculateAccurateCuttingTimeStatistics()
        {
            try
            {
                if (_gcodeSegments == null || _gcodeSegments.Count ==0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ No segments available for time calculation");
                    return (0,0,0,0);
                }

                double linearTime =0;  // minutes
                double rapidTime =0;   // minutes  
                double arcTime =0;     // minutes

                // Default feed rates if not specified in G-code (mm/min)
                const double DEFAULT_RAPID_FEEDRATE =3000.0;   // Typical rapid speed
                const double DEFAULT_LINEAR_FEEDRATE =500.0;   // Conservative cutting speed
                const double DEFAULT_ARC_FEEDRATE =300.0;      // Conservative arc speed

                int segmentCount =0;
                int segmentsWithFeedRate =0;
                double totalDistanceProcessed =0;

                System.Diagnostics.Debug.WriteLine($"🔍 Processing {_gcodeSegments.Count} segments for time calculation");

                foreach (var segment in _gcodeSegments)
                {
                    segmentCount++;
                    
                    // Get cached distance or calculate it
                    double distance = segment.GetActualDistance();
                    if (distance <=0)
                    {
                        // Calculate if not cached
                        var parser = new GCodeParser();
                        distance = parser.CalculateSegmentDistance(segment);
                        segment.SetCalculatedDistance(distance);
                    }
                    
                    if (distance <=0) 
                    {
                        continue; // Skip zero-distance segments
                    }

                    totalDistanceProcessed += distance;

                    // Get modal feed rate from segment (comes from parser)
                    double feedRate = segment.FeedRate;
                    bool usingDefaultFeedRate = false;
                    
                    // Use default feed rates if not specified or invalid
                    if (feedRate <=0)
                    {
                        usingDefaultFeedRate = true;
                        switch (segment.MovementType)
                        {
                            case GCodeMovementType.Rapid:
                                feedRate = DEFAULT_RAPID_FEEDRATE;
                                break;
                            case GCodeMovementType.Linear:
                                feedRate = DEFAULT_LINEAR_FEEDRATE;
                                break;
                            case GCodeMovementType.Arc:
                                feedRate = DEFAULT_ARC_FEEDRATE;
                                break;
                            default:
                                feedRate = DEFAULT_LINEAR_FEEDRATE;
                                break;
                        }
                    }
                    else
                    {
                        segmentsWithFeedRate++;
                    }

                    // Calculate time in minutes: distance (mm) / feedRate (mm/min) = time (min)
                    double segmentTime = distance / feedRate;

                    switch (segment.MovementType)
                    {
                        case GCodeMovementType.Linear:
                            linearTime += segmentTime;
                            break;
                        case GCodeMovementType.Rapid:
                            rapidTime += segmentTime;
                            break;
                        case GCodeMovementType.Arc:
                            arcTime += segmentTime;
                            break;
                    }

                    // Debug every 100th segment or segments with high time
                    if (segmentCount % 100 == 0 || segmentTime > 1.0)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 Segment {segmentCount}: {segment.MovementType}, Distance: {distance:F2}mm, FeedRate: {feedRate:F0}mm/min{(usingDefaultFeedRate ? " (default)" : "")}, Time: {segmentTime * 60:F1}sec");
                    }
                }

                double totalTime = linearTime + rapidTime + arcTime;

                // Final summary
                System.Diagnostics.Debug.WriteLine($"📊 TIME CALCULATION SUMMARY:");
                System.Diagnostics.Debug.WriteLine($"📊 Total segments processed: {segmentCount}");
                System.Diagnostics.Debug.WriteLine($"📊 Segments with feed rate: {segmentsWithFeedRate}");
                System.Diagnostics.Debug.WriteLine($"📊 Total distance: {totalDistanceProcessed:F1}mm");
                System.Diagnostics.Debug.WriteLine($"📊 Linear time: {linearTime:F2}min ({linearTime *60:F0}sec)");
                System.Diagnostics.Debug.WriteLine($"📊 Rapid time: {rapidTime:F2}min ({rapidTime *60:F0}sec)");
                System.Diagnostics.Debug.WriteLine($"📊 Arc time: {arcTime:F2}min ({arcTime *60:F0}sec)");
                System.Diagnostics.Debug.WriteLine($"📊 Total time: {totalTime:F2}min ({totalTime *60:F0}sec)");

                return (linearTime, rapidTime, arcTime, totalTime);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CalculateAccurateCuttingTimeStatistics error: {ex.Message}");
                return (0,0,0,0);
            }
        }

        /// <summary>
        /// Calculate estimated time based on distances and default feed rates
        /// </summary>
        private (double LinearTime, double RapidTime, double ArcTime, double TotalTime) CalculateEstimatedTimes(
            double linearDistance, double rapidDistance, double arcDistance)
        {
            // Default feed rates (mm/min)
            const double DEFAULT_RAPID_FEEDRATE =3000.0;
            const double DEFAULT_LINEAR_FEEDRATE =500.0;
            const double DEFAULT_ARC_FEEDRATE =300.0;

            // Calculate times in minutes
            double linearTime = linearDistance / DEFAULT_LINEAR_FEEDRATE;
            double rapidTime = rapidDistance / DEFAULT_RAPID_FEEDRATE;
            double arcTime = arcDistance / DEFAULT_ARC_FEEDRATE;
            double totalTime = linearTime + rapidTime + arcTime;

            return (linearTime, rapidTime, arcTime, totalTime);
        }

        #endregion
    }

    /// <summary>
    /// File statistics data structure
    /// </summary>
    public class FileStatistics
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public int LineCount { get; set; }
        public int SegmentCount { get; set; }
        public double XRange { get; set; }
        public double YRange { get; set; }
        public double ZRange { get; set; }
        public double XTotalDistance { get; set; }
        public double YTotalDistance { get; set; }
        public double ZTotalDistance { get; set; }

        // Cutting distance statistics
        public double LinearCuttingDistance { get; set; }
        public double RapidMovementDistance { get; set; }
        public double ArcCuttingDistance { get; set; }
        public double TotalCuttingDistance { get; set; }

        /// <summary>
        /// Get formatted file size string
        /// </summary>
        public string FormattedFileSize
        {
            get
            {
                return FileSize <1024 ? $"{FileSize} B" :
                       FileSize <1024 *1024 ? $"{FileSize /1024.0:F1} KB" :
                       $"{FileSize / (1024.0 *1024.0):F2} MB";
            }
        }

        /// <summary>
        /// Get formatted cutting distance strings
        /// </summary>
        public string FormattedLinearDistance => $"{LinearCuttingDistance:F1}mm";
        public string FormattedRapidDistance => $"{RapidMovementDistance:F1}mm";
        public string FormattedArcDistance => $"{ArcCuttingDistance:F1}mm";
        public string FormattedTotalDistance => $"{TotalCuttingDistance:F1}mm";
    }
}