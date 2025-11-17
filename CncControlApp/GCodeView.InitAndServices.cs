using System;
using System.Windows;
using System.Windows.Threading;
using CncControlApp.Controls;   // MessageDialog
using CncControlApp.Managers;  // GCodeOverlayManager
using System.Windows.Controls; // ✅ TextBlock için

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void InitializeGCodeWorkspace()
        {
            _viewportManager = new ViewportManager(TopViewCanvas, FrontViewCanvas, RightViewCanvas, IsometricViewCanvas);
            _fileService = new GCodeFileService(_viewportManager);
            _overlayManager = new GCodeOverlayManager(TopViewCanvas, TopViewOverlayCanvas);
            var visualization = new GCodeVisualization(_viewportManager);
            visualization.InitializeStaticOverlays();
            _visualization = visualization;
            HookFileServiceEvents();
            HookExecutionEvents();
            GCodeListBox.ItemsSource = DisplayGCodeLines;
            DataContext = this;
            EnsureExecutionBindings();
            SetupViewportInteraction();
            if (App.MainController != null)
            {
                App.MainController.PropertyChanged -= MainController_PropertyChanged;
                App.MainController.PropertyChanged += MainController_PropertyChanged;
            }
        }
        private void HookFileServiceEvents()
        {
            if (_fileService == null || _fileServiceEventsHooked) return;
            _fileService.FileInformationUpdated += OnFileInformationUpdated;
            _fileService.DistanceStatisticsUpdated += OnDistanceStatisticsUpdated;
            _fileService.CuttingDistanceStatisticsUpdated += OnCuttingDistanceStatisticsUpdated;
            _fileService.FileLoadCompleted += OnFileLoadCompleted;
            _fileService.ErrorOccurred += OnErrorOccurred;
            _fileService.LoadingProgressChanged += OnLoadingProgressChanged;
            _fileService.CuttingTimeStatisticsUpdated += OnCuttingTimeStatisticsUpdated;
            _fileServiceEventsHooked = true;
        }

        private void HookExecutionEvents()
        {
            if (App.MainController?.GCodeManager != null)
            {
                App.MainController.GCodeManager.ExecutionCompleted -= OnExecutionCompleted;
                App.MainController.GCodeManager.ExecutionCompleted += OnExecutionCompleted;
            }
        }

        private void UnhookExecutionEvents()
        {
            if (App.MainController?.GCodeManager != null)
            {
                App.MainController.GCodeManager.ExecutionCompleted -= OnExecutionCompleted;
            }
        }
        private void UnhookFileServiceEvents()
        {
            if (_fileService == null || !_fileServiceEventsHooked) return;
            _fileService.FileInformationUpdated -= OnFileInformationUpdated;
            _fileService.DistanceStatisticsUpdated -= OnDistanceStatisticsUpdated;
            _fileService.CuttingDistanceStatisticsUpdated -= OnCuttingDistanceStatisticsUpdated;
            _fileService.FileLoadCompleted -= OnFileLoadCompleted;
            _fileService.ErrorOccurred -= OnErrorOccurred;
            _fileService.LoadingProgressChanged -= OnLoadingProgressChanged;
            _fileService.CuttingTimeStatisticsUpdated -= OnCuttingTimeStatisticsUpdated;
            _fileServiceEventsHooked = false;
        }
        private void EnsureExecutionBindings()
        {
            if (App.MainController == null) return;
            if (RemainingTimeTextBlock != null && !ReferenceEquals(RemainingTimeTextBlock.DataContext, App.MainController))
                RemainingTimeTextBlock.DataContext = App.MainController;
            if (ExecutionTimeTextBlock != null && !ReferenceEquals(ExecutionTimeTextBlock.DataContext, App.MainController))
                ExecutionTimeTextBlock.DataContext = App.MainController;
            if (ProgressTextBlock != null && !ReferenceEquals(ProgressTextBlock.DataContext, App.MainController))
                ProgressTextBlock.DataContext = App.MainController;
            // CurrentLineTextBlock was removed during panel reorganization - no longer exists in XAML
        }
        private void SetupViewportInteraction()
        {
            try { _viewportManager?.SetupViewportMouseEvents(); } catch { }
        }
        private void OnErrorOccurred(string title, string message)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { MessageDialog.ShowError(title, message); } catch { }
                }), DispatcherPriority.Background);
            }
            catch { }
        }
        private void OnLoadingProgressChanged(double percentage, string statusMessage)
        {
            try { } catch { }
        }
    }
}