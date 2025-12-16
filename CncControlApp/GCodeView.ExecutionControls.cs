using System;
using System.Windows;
using System.Windows.Media;
using CncControlApp.Controls; // MessageDialog
using CncControlApp.Services; // GCodeRunLineLogger

namespace CncControlApp
{
    public partial class GCodeView
    {
        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController == null)
                {
                    ShowErrorMessage("MainController is not available", "System Error");
                    return;
                }
                if (!App.MainController.IsConnected)
                {
                    ShowErrorMessage("CNC machine is not connected. Please connect first.", "Connection Required");
                    return;
                }

                bool running = App.MainController.IsGCodeRunning;
                bool hold = App.MainController.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);

                // If already running or in hold, treat RunButton as Stop/Pause
                if (running || hold)
                {
                    await RequestStopAsync(RunButton);
                    // ensure visual state after stop/continue decision
                    UpdateExecutionControlButtons();
                    return;
                }

                if (!App.MainController.CanStartExecution)
                {
                    ShowErrorMessage("No G-Code file loaded or not ready.", "No G-Code Loaded");
                    return;
                }

                // ✅ FIT CHECK: Prevent run if part doesn't fit on table
                var (fits, fitStatus, fitDetails) = CheckLiveFitStatus();
                if (!fits && fitStatus != "-" && fitStatus != "NO TABLE")
                {
                    ShowErrorMessage(
                        $"Part does not fit within table bounds!\n\n{fitDetails}\n\nPlease adjust rotation or position in Rotation Popup before running.",
                        "Part Does Not Fit"
                    );
                    return;
                }

                if (RunButton != null) RunButton.IsEnabled = false;

                // Start line-by-line logger
                try { Services.GCodeRunLineLogger.Instance.Begin(App.MainController); } catch { }

                // Track execution start
                _executionStartTime = DateTime.Now;
                _currentFileName = _fileService?.CurrentFileName ?? "Unknown";

                bool executionStarted = await App.MainController.RunGCodeAsync();
               
                if (executionStarted)
                {
                    UpdateExecutionControlButtons();
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = "G-Code Execution Running - Monitor progress";
                        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
                    }
                    var mw = Window.GetWindow(this) as MainWindow;
                    var coordinatesView = mw?.FindName("CoordinatesView") as CoordinatesView;
                    coordinatesView?.CloseActiveNumberPad();
                }
                else
                {
                    // ✅ REVERTED: Let RunGCodeAsync return false legitimately
                    // The real issue is likely in WaitForIdleAsync timing
                    ShowErrorMessage("Failed to start G-Code execution.", "Execution Failed");
                    if (RunButton != null) RunButton.IsEnabled = true;

                    // Stop logger if start failed
                    try { Services.GCodeRunLineLogger.Instance.End(); } catch { }
                }

                // ensure visual state after any path
                UpdateExecutionControlButtons();
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Error starting execution:\n{ex.Message}", "Execution Error");
                if (RunButton != null) RunButton.IsEnabled = true;
                UpdateExecutionControlButtons();
                try { Services.GCodeRunLineLogger.Instance.End(); } catch { }
            }
        }

        private async System.Threading.Tasks.Task RequestStopAsync(System.Windows.Controls.Button sourceButton)
        {
            bool prevAuto = false; // capture outside try so it's visible in catch/finally
            try
            {
                var mc = App.MainController;
                if (mc == null || !mc.IsConnected) return;
                if (!mc.IsGCodeRunning && !mc.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase)) return;

                if (sourceButton != null) sourceButton.IsEnabled = false;

                // PREVENT duplicate auto popups by disabling them BEFORE we trigger Hold
                prevAuto = mc.DisableAutoStopPopup;
                mc.DisableAutoStopPopup = true;

                // 1) First send the stop request (Feed Hold) to machine IMMEDIATELY
                AddLogMessage("> ⏸️ STOP sinyali gönderiliyor (!) - popup açılıyor");
                bool stopSent = await mc.GCodeManager.SendControlCharacterAsync('!');

                if (!stopSent)
                {
                    AddLogMessage("> ⚠️ Feed Hold (!) gönderilemedi - alternatif deneniyor");
                    // Try direct stop command
                    stopSent = await mc.SendGCodeCommandAsync("!");
                }

                if (stopSent)
                {
                    AddLogMessage("> ✅ Feed Hold (!) başarıyla gönderildi");
                }
                else
                {
                    AddLogMessage("> ⚠️ Feed Hold (!) gönderilemedi - normal stop süreci devam edecek");
                }

                // 3) Show confirmation popup (hold command already sent above)
                bool confirmStop = MessageDialog.ShowStopConfirm(
                    "G-Code Yürütme Duraklatıldı",
                    "İşlem güvenli şekilde duraklatıldı ve makine bekliyor.\n\n" +
                    "Bir işlem seçin:\n" +
                    "• STOP Run: Yürütmeyi tamamen durdur ve güvenli duruş sekansını çalıştır.\n" +
                    "• Continue: Duraklatmadan devam et (kaldığı yerden).\n\n" +
                    "Not: STOP Run, tüm tamponları temizler ve sistemi güvenli hale getirir.");

                // 4) Act on decision
                if (confirmStop)
                {
                    mc.ConfirmStopAndRunSequence();
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = "Stopping... running complete stop sequence";
                        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
                    }

                    // Stop logger when stop sequence completes
                    Action restoreAuto = null;
                    restoreAuto = () =>
                    {
                        try { mc.StopSequenceCompleted -= restoreAuto; } catch { }
                        mc.DisableAutoStopPopup = prevAuto;
                        try { Services.GCodeRunLineLogger.Instance.End(); } catch { }
                    };
                    mc.StopSequenceCompleted += restoreAuto;
                }
                else
                {
                    await mc.CancelStopAndContinueAsync();
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = "Resuming after pause";
                        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
                    }
                    // Restore immediately for continue
                    mc.DisableAutoStopPopup = prevAuto;
                }

                UpdateExecutionControlButtons();
                if (sourceButton != null) sourceButton.IsEnabled = true;

                // Note: for STOP path, restoration is handled by StopSequenceCompleted handler above
            }
            catch (Exception ex)
            {
                AddLogMessage($"> ❌ Stop işlemi hatası: {ex.Message}");
                if (sourceButton != null) sourceButton.IsEnabled = true;
                // On error, restore auto-popup immediately to avoid being stuck
                try { var mc2 = App.MainController; if (mc2 != null) mc2.DisableAutoStopPopup = prevAuto; } catch { }
                try { Services.GCodeRunLineLogger.Instance.End(); } catch { }
            }
            finally
            {
                // Final visual sync
                UpdateExecutionControlButtons();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Keep for safety if XAML still wires it; route to common stop logic
            await RequestStopAsync(StopButton);
        }

        private void UpdateExecutionControlButtons()
        {
            try
            {
                if (App.MainController == null) return;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    bool running = App.MainController.IsGCodeRunning;
                    bool hold = App.MainController.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
                    bool connected = App.MainController.IsConnected;
                    bool loaded = App.MainController.IsGCodeLoaded;
                    bool canResume = App.MainController.CanResumeFromLine;

                    if (RunButton != null)
                    {
                        if (running || hold)
                        {
                            // Turn RUN into STOP/PAUSE
                            RunButton.Content = "STOP / PAUSE";
                            RunButton.Tag = "⏹️";
                            RunButton.Foreground = new SolidColorBrush(Colors.White);
                            RunButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF3B30"));
                            RunButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB02A37"));
                            RunButton.IsEnabled = connected; // allow stopping while running/hold
                        }
                        else
                        {
                            // Idle state – show RUN
                            RunButton.Content = "RUN";
                            RunButton.Tag = "▶️";
                            RunButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF06E43E"));
                            RunButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF474A50"));
                            RunButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2C"));
                            RunButton.IsEnabled = connected && loaded && !hold;
                        }
                    }
                    
                    // Başlama Satırı paneli - sadece G-Code yüklüyse görünür
                    if (ResumeButtonsPanel != null)
                    {
                        ResumeButtonsPanel.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    // Başlama Satırı butonu ve satır numarası gösterimi
                    if (StartFromLineButton != null)
                    {
                        StartFromLineButton.IsEnabled = connected && loaded && !running && !hold;
                        
                        // Satır numarasını güncelle
                        int lastLine = App.MainController?.LastStoppedLineIndex ?? -1;
                        int displayLine = (lastLine >= 0) ? (lastLine + 1) : 0;
                        
                        if (StartLineNumberText != null)
                        {
                            StartLineNumberText.Text = displayLine.ToString();
                        }
                        
                        // Uyarı etiketi - kayıtlı satır varsa göster
                        if (ResumeWarningLabel != null)
                        {
                            ResumeWarningLabel.Visibility = (lastLine >= 0) ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }

                    // Hide legacy Stop button if present
                    if (StopButton != null)
                    {
                        StopButton.Visibility = Visibility.Collapsed;
                        StopButton.IsEnabled = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }
        
        private void AddLogMessage(string message)
        {
            try
            {
                App.MainController?.AddLogMessage(message);
            }
            catch { }
        }

        private void ShowErrorMessage(string message, string title, bool suppress = false)
        {
            try
            {
                var mc = App.MainController;
                if (mc != null && mc.IsInPostStopGracePeriod)
                {
                    mc.AddLogMessage($"> 🔇 (Grace) Popup bastırıldı: {title} - {message}");
                    return;
                }
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    var prev = MessageDialog.SuppressErrorPopups;
                    if (suppress) MessageDialog.SuppressErrorPopups = true;
                    try { MessageDialog.ShowError(title, message); }
                    finally { MessageDialog.SuppressErrorPopups = prev; }
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch { }
        }
        
        /// <summary>
        /// Başlama Satırı butonunun görünürlüğünü günceller
        /// </summary>
        private void UpdateResumeButtonVisibility()
        {
            try
            {
                if (StartFromLineButton == null) return;
                
                int lastLine = App.MainController?.LastStoppedLineIndex ?? -1;
                bool isConnected = App.MainController?.IsConnected ?? false;
                bool hasGCode = App.MainController?.CanStartExecution ?? false;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Panel görünürlüğü - sadece G-Code yüklüyse görünür
                    if (ResumeButtonsPanel != null)
                    {
                        ResumeButtonsPanel.Visibility = hasGCode ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    // Başlama Satırı butonu - bağlı ve G-Code yüklüyse aktif
                    StartFromLineButton.IsEnabled = isConnected && hasGCode;
                    
                    // Satır numarasını güncelle
                    int displayLine = (lastLine >= 0) ? (lastLine + 1) : 0;
                    
                    if (StartLineNumberText != null)
                    {
                        StartLineNumberText.Text = displayLine.ToString();
                    }
                    
                    // Uyarı etiketi - kayıtlı satır varsa göster
                    if (ResumeWarningLabel != null)
                    {
                        ResumeWarningLabel.Visibility = (lastLine >= 0) ? Visibility.Visible : Visibility.Collapsed;
                    }
                }));
            }
            catch { }
        }
        
        /// <summary>
        /// Başlama Satırı butonu click handler - kayıtlı satır varsa onu kullanır, yoksa dialog açar
        /// </summary>
        private async void StartFromLineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController == null)
                {
                    ShowErrorMessage("MainController is not available", "System Error");
                    return;
                }
                
                if (!App.MainController.IsConnected)
                {
                    ShowErrorMessage("CNC bağlı değil. Önce bağlanın.", "Bağlantı Gerekli");
                    return;
                }
                
                if (!App.MainController.CanStartExecution)
                {
                    ShowErrorMessage("G-Code dosyası yüklü değil.", "G-Code Gerekli");
                    return;
                }
                
                // Kayıtlı satır numarasını al
                int lastLine = App.MainController.LastStoppedLineIndex;
                int defaultLine = (lastLine >= 0) ? (lastLine + 1) : 1; // 1-based for dialog
                
                // Satır numarası sor (kayıtlı satır varsa onu default olarak göster)
                var lineInput = await ShowLineInputDialogAsync(defaultLine);
                if (lineInput == null || lineInput < 1)
                {
                    return; // Kullanıcı iptal etti
                }
                
                int lineIndex = lineInput.Value - 1; // 0-based index
                
                // LastStoppedLineIndex'i ayarla ve resume dialog'u göster
                App.MainController.LastStoppedLineIndex = lineIndex;
                
                bool result = await App.MainController.ShowResumeFromLineDialogAsync();
                
                if (result)
                {
                    UpdateExecutionControlButtons();
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Satırdan başlama hatası:\n{ex.Message}", "Hata");
            }
        }
        
        /// <summary>
        /// Satır numarası giriş dialogu gösterir
        /// </summary>
        private async System.Threading.Tasks.Task<int?> ShowLineInputDialogAsync(int defaultLine = 1)
        {
            int? result = null;
            
            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Controls.LineInputDialog(defaultLine);
                dialog.Owner = Window.GetWindow(this);
                
                if (dialog.ShowDialog() == true)
                {
                    result = dialog.LineNumber;
                }
            });
            
            return result;
        }
    }
}