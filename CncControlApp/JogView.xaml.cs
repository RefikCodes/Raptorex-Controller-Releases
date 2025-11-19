using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CncControlApp.Handlers;
using CncControlApp.Helpers; // 🆕 Use EventHandlerHelper
using CncControlApp.Controls;

namespace CncControlApp
{
    public partial class JogView : UserControl
    {
 private readonly JogMovementHandler _jogMovementHandler;
        private readonly ControlButtonHandler _controlButtonHandler;
  private readonly SliderHandler _sliderHandler;
        private readonly StepControlHandler _stepControlHandler;

        private PanelJogView _panelJogView;

        // 🆕 Helper for consistent logging
        private void Log(string message) => App.MainController?.AddLogMessage(message);

        public JogView()
        {
       InitializeComponent();

            if (App.MainController == null)
    {
       return;
 }

            this.DataContext = App.MainController;

try
    {
        _jogMovementHandler = new JogMovementHandler(App.MainController);
       _controlButtonHandler = new ControlButtonHandler(App.MainController);
             _sliderHandler = new SliderHandler(App.MainController);

     // Removed A-axis controls initialization
    if (XYZStepButtonGrid != null && XYZContinuousButton != null)
         {
_stepControlHandler = new StepControlHandler(
           App.MainController,
     XYZStepButtonGrid,
           XYZContinuousButton,
       null,  // AContinuousButton removed
        null); // AStepButtonGrid removed
 }

     this.Loaded += JogView_Loaded;
            }
       catch (Exception ex)
        {
    Log($"> ❌ HATA: JogView başlatma - {ex.Message}");
            }
        }

        private async void JogView_Loaded(object sender, RoutedEventArgs e)
        {
 try
            {
                if (_stepControlHandler != null)
     {
              _stepControlHandler.UpdateXYZStepButtonSelection();
        // Removed A-axis step button selection update
    }

                if (App.MainController != null)
        {
             App.MainController.JogSpeedPercentage = 50;
                  // Removed A-axis speed percentage initialization

      await Task.Delay(3000);
           await JogDebugHelper.TestSystemDiagnosis(App.MainController);
                }
    }
            catch (Exception ex)
            {
      Log($"> ❌ HATA: JogView yükleme - {ex.Message}");
            }
        }

        // ONLY XY OVERLAY
  private void SwitchToPanelJogButton_Checked(object sender, RoutedEventArgs e)
        {
      EventHandlerHelper.SafeHandle(() =>
    {
if (_panelJogView == null)
 {
   _panelJogView = new PanelJogView();
      }

       if (_panelJogView.Parent is Panel oldParent)
 {
   oldParent.Children.Remove(_panelJogView);
    }

   _panelJogView.HorizontalAlignment = HorizontalAlignment.Stretch;
     _panelJogView.VerticalAlignment = VerticalAlignment.Stretch;

          PanelJogOverlayHostXY.Visibility = Visibility.Visible;
  PanelJogOverlayHostXY.Children.Clear();
PanelJogOverlayHostXY.Children.Add(_panelJogView);
         _panelJogView.Open();
 }, "PanelJog_Open", Log);
        }

     private void SwitchToPanelJogButton_Unchecked(object sender, RoutedEventArgs e)
        {
  EventHandlerHelper.SafeHandle(() =>
{
   if (_panelJogView != null)
       {
   _panelJogView.EnterIndicatorMode();
     }

PanelJogOverlayHostXY.Children.Clear();
       PanelJogOverlayHostXY.Visibility = Visibility.Collapsed;
     }, "PanelJog_Close", Log);
  }

        #region Jog Movement Events - 🆕 REFACTORED: Using EventHandlerHelper pattern
   
        // 🆕 Mouse handlers - consolidated with SafeHandleAsync
        private async void JogXPlus_Start(object sender, MouseButtonEventArgs e) =>
await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogXPlusStart(sender, e), "JogXPlus_Start", Log);
        
    private async void JogXMinus_Start(object sender, MouseButtonEventArgs e) =>
  await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogXMinusStart(sender, e), "JogXMinus_Start", Log);
    
        private async void JogYPlus_Start(object sender, MouseButtonEventArgs e) =>
     await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogYPlusStart(sender, e), "JogYPlus_Start", Log);
        
      private async void JogYMinus_Start(object sender, MouseButtonEventArgs e) =>
      await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogYMinusStart(sender, e), "JogYMinus_Start", Log);
        
        private async void JogZPlus_Start(object sender, MouseButtonEventArgs e) =>
    await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogZPlusStart(sender, e), "JogZPlus_Start", Log);
  
        private async void JogZMinus_Start(object sender, MouseButtonEventArgs e) =>
   await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogZMinusStart(sender, e), "JogZMinus_Start", Log);

        // Removed A-axis jog methods
      
        private async void Jog_Stop(object sender, MouseButtonEventArgs e) =>
            await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogStop(sender, e), "Jog_Stop", Log);
        
   private async void Jog_Stop_MouseLeave(object sender, MouseEventArgs e) =>
            await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogStopMouseLeave(sender, e), "Jog_Stop_MouseLeave", Log);
        
        private async void Jog_MouseMove(object sender, MouseEventArgs e) =>
   await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogMouseMove(sender, e), "Jog_MouseMove", Log);

        // 🆕 Touch handlers - consolidated with SafeHandleAsync
        private async void JogXPlus_TouchStart(object sender, TouchEventArgs e) =>
       await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogXPlusTouchStart(sender, e), "JogXPlus_Touch", Log);
      
        private async void JogXMinus_TouchStart(object sender, TouchEventArgs e) =>
     await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogXMinusTouchStart(sender, e), "JogXMinus_Touch", Log);
        
        private async void JogYPlus_TouchStart(object sender, TouchEventArgs e) =>
            await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogYPlusTouchStart(sender, e), "JogYPlus_Touch", Log);
  
        private async void JogYMinus_TouchStart(object sender, TouchEventArgs e) =>
            await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogYMinusTouchStart(sender, e), "JogYMinus_Touch", Log);
        
        private async void JogZPlus_TouchStart(object sender, TouchEventArgs e) =>
 await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogZPlusTouchStart(sender, e), "JogZPlus_Touch", Log);
        
        private async void JogZMinus_TouchStart(object sender, TouchEventArgs e) =>
await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogZMinusTouchStart(sender, e), "JogZMinus_Touch", Log);
    
        // Removed A-axis touch methods
 
        private async void Jog_TouchStop(object sender, TouchEventArgs e) =>
   await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogTouchStop(sender, e), "Jog_TouchStop", Log);
        
      private async void Jog_TouchMove(object sender, TouchEventArgs e) =>
            await EventHandlerHelper.SafeHandleAsync(() => _jogMovementHandler?.HandleJogTouchMove(sender, e), "Jog_TouchMove", Log);
        
        #endregion

        #region Control Buttons - 🆕 REFACTORED: Using EventHandlerHelper pattern
   
        private async void SpindleButton_Click(object sender, RoutedEventArgs e) =>
await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleSpindleButtonClick(sender, e), "Spindle_Control", Log);
        
 private async void CoolantButton_Click(object sender, RoutedEventArgs e) =>
     await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleCoolantButtonClick(sender, e), "Coolant_Control", Log);
        
   private async void MistButton_Click(object sender, RoutedEventArgs e) =>
   await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleMistButtonClick(sender, e), "Mist_Control", Log);
        
  private async void LightsButton_Click(object sender, RoutedEventArgs e) =>
    await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleLightsButtonClick(sender, e), "Lights_Control", Log);
    
        private async void ToolChangeButton_Click(object sender, RoutedEventArgs e) =>
   await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleToolChangeButtonClick(sender, e), "ToolChange_Control", Log);
        
     private void ConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            EventHandlerHelper.SafeHandle(() =>
  {
       Log($"> 🖥️ Console butonu tıklandı");

    if (sender is ToggleButton toggleButton)
    {
       toggleButton.IsChecked = false;
       }

    var consoleWindow = new ConsoleWindow();
         consoleWindow.Show();

       Log($"> ✅ Console penceresi açıldı");
       }, "Console_Open", Log);
 }
        
        private async void VacuumButton_Click(object sender, RoutedEventArgs e) =>
 await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleVacuumButtonClick(sender, e), "Vacuum_Control", Log);
    
   private async void AirBlastButton_Click(object sender, RoutedEventArgs e) =>
  await EventHandlerHelper.SafeHandleAsync(() => _controlButtonHandler?.HandleAirBlastButtonClick(sender, e), "AirBlast_Control", Log);
        
        #endregion

        #region Spindle Slider - 🆕 REFACTORED: Using EventHandlerHelper pattern
        
 private void SpindleSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderValueChanged(sender, e), "SpindleSpeed_Value", Log);
  
        private void SpindleSpeedSlider_TouchDown(object sender, TouchEventArgs e) =>
    EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderTouchDown(sender, e), "SpindleSpeed_TouchDown", Log);
        
        private void SpindleSpeedSlider_TouchMove(object sender, TouchEventArgs e) =>
      EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderTouchMove(sender, e), "SpindleSpeed_TouchMove", Log);
   
  private void SpindleSpeedSlider_TouchUp(object sender, TouchEventArgs e) =>
EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderTouchUp(sender, e), "SpindleSpeed_TouchUp", Log);

    private void SpindleSpeedSlider_TouchLeave(object sender, TouchEventArgs e) =>
    EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderTouchLeave(sender, e), "SpindleSpeed_TouchLeave", Log);
        
        private void SpindleSpeedSlider_MouseDown(object sender, MouseButtonEventArgs e) =>
       EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderMouseDown(sender, e), "SpindleSpeed_MouseDown", Log);
      
        private void SpindleSpeedSlider_MouseMove(object sender, MouseEventArgs e) =>
 EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderMouseMove(sender, e), "SpindleSpeed_MouseMove", Log);
        
  private void SpindleSpeedSlider_MouseUp(object sender, MouseButtonEventArgs e) =>
 EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleSpindleSpeedSliderMouseUp(sender, e), "SpindleSpeed_MouseUp", Log);
   
 #endregion

        #region Jog Speed Slider - 🆕 REFACTORED: Using EventHandlerHelper pattern
   
  private void JogSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
   EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderValueChanged(sender, e), "JogSpeed_Value", Log);

  private void JogSpeedSlider_TouchDown(object sender, TouchEventArgs e) =>
  EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderTouchDown(sender, e), "JogSpeed_TouchDown", Log);
        
        private void JogSpeedSlider_TouchMove(object sender, TouchEventArgs e) =>
      EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderTouchMove(sender, e), "JogSpeed_TouchMove", Log);
      
 private void JogSpeedSlider_TouchUp(object sender, TouchEventArgs e) =>
            EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderTouchUp(sender, e), "JogSpeed_TouchUp", Log);
        
   private void JogSpeedSlider_TouchLeave(object sender, TouchEventArgs e) =>
        EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderTouchLeave(sender, e), "JogSpeed_TouchLeave", Log);
        
    private void JogSpeedSlider_MouseDown(object sender, MouseButtonEventArgs e) =>
    EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderMouseDown(sender, e), "JogSpeed_MouseDown", Log);
        
 private void JogSpeedSlider_MouseMove(object sender, MouseEventArgs e) =>
   EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderMouseMove(sender, e), "JogSpeed_MouseMove", Log);
     
        private void JogSpeedSlider_MouseUp(object sender, MouseButtonEventArgs e) =>
 EventHandlerHelper.SafeHandle(() => _sliderHandler?.HandleJogSpeedSliderMouseUp(sender, e), "JogSpeed_MouseUp", Log);
        
/// <summary>
        /// Reset jog speed to 50%
        /// </summary>
   private void JogSpeedResetButton_Click(object sender, RoutedEventArgs e)
        {
            EventHandlerHelper.SafeHandle(() =>
            {
          if (App.MainController != null)
{
       App.MainController.JogSpeedPercentage = 50;
   Log($"> ✅ Jog hızı %50 olarak sıfırlandı");
      }
        }, "JogSpeed_Reset", Log);
        }

        #endregion

        #region Step Controls - 🆕 REFACTORED: Using EventHandlerHelper pattern
   
  private void XYZContinuous_Click(object sender, RoutedEventArgs e)
      {
            EventHandlerHelper.SafeHandle(() =>
  {
  if (_stepControlHandler != null)
         _stepControlHandler.HandleXYZContinuousClick(sender, e);

       if (_jogMovementHandler != null)
  {
              _jogMovementHandler.ResetXYZJoggingState();
            }
    }, "XYZContinuous_Click", Log);
        }
        
        private void XYZStepSize_Click(object sender, RoutedEventArgs e) =>
     EventHandlerHelper.SafeHandle(() => _stepControlHandler?.HandleXYZStepSizeClick(sender, e), "XYZStepSize_Click", Log);
        
        private void XYZStepSize_TouchDown(object sender, TouchEventArgs e) =>
    EventHandlerHelper.SafeHandle(() => _stepControlHandler?.HandleXYZStepSizeTouchDown(sender, e), "XYZStepSize_Touch", Log);
    
        private void XYZStepSize_TouchUp(object sender, TouchEventArgs e) =>
     EventHandlerHelper.SafeHandle(() => _stepControlHandler?.HandleXYZStepSizeTouchUp(sender, e), "XYZStepSize_TouchUp", Log);
        
        private void XYZStepSize_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
            EventHandlerHelper.SafeHandle(() => _stepControlHandler?.HandleXYZStepSizePreviewMouseDown(sender, e), "XYZStepSize_Mouse", Log);
        
     // Removed A-axis step control methods
        
        #endregion

  private void btnSpindle_Checked(object sender, RoutedEventArgs e) { }
    }
}