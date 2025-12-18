using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CncControlApp
{
    /// <summary>
    /// GitHub Releases üzerinden otomatik güncelleme kontrolü
    /// Rate limit sorunu yaşamamak için redirect yöntemi kullanılır
    /// Otomatik indirme, kurulum ve yeniden başlatma desteği
    /// </summary>
    public static class UpdateChecker
    {
        private const string GITHUB_REPO = "RefikCodes/Raptorex-Controller-Releases";
        private const string RELEASES_PAGE_URL = "https://github.com/{0}/releases/latest";
        private const string TEMP_INSTALLER_NAME = "RaptorexController_Update.exe";

        /// <summary>
        /// Mevcut uygulama versiyonunu alır
        /// </summary>
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var version = assembly.GetName().Version;
                    return version ?? new Version(1, 0, 0, 0);
                }
                catch
                {
                    return new Version(1, 0, 0, 0);
                }
            }
        }

        /// <summary>
        /// GitHub'dan en son sürümü kontrol eder (redirect yöntemi - rate limit yok)
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                string releasesUrl = string.Format(RELEASES_PAGE_URL, GITHUB_REPO);
                
                // HttpClient ile redirect'i takip etmeden son URL'i al
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "RaptorexController-UpdateChecker");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var response = await client.GetAsync(releasesUrl);
                    
                    // 301/302 redirect olmalı
                    if (response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.MovedPermanently)
                    {
                        string redirectUrl = response.Headers.Location?.ToString();
                        
                        if (!string.IsNullOrEmpty(redirectUrl))
                        {
                            // URL'den tag'i çıkar: .../releases/tag/v2.0.6 -> v2.0.6
                            var match = Regex.Match(redirectUrl, @"/tag/v?([0-9.]+)");
                            if (match.Success)
                            {
                                string latestVersionStr = match.Groups[1].Value;
                                
                                if (Version.TryParse(NormalizeVersion(latestVersionStr), out Version latestVersion))
                                {
                                    bool hasUpdate = latestVersion > CurrentVersion;

                                    // Aday dosya adlarını sırayla kontrol et (yeni ve eski formatlar)
                                    string shortVersion = GetShortVersion(latestVersionStr);
                                    string[] candidates = new[]
                                    {
                                        $"https://github.com/{GITHUB_REPO}/releases/download/v{latestVersionStr}/RaptorexController-{latestVersionStr}-Setup.exe", // yeni dash format
                                        $"https://github.com/{GITHUB_REPO}/releases/download/v{latestVersionStr}/RaptorexController_Setup_{latestVersionStr}.exe",   // eski underscore (tam)
                                        $"https://github.com/{GITHUB_REPO}/releases/download/v{latestVersionStr}/RaptorexController_Setup_{shortVersion}.exe"        // eski underscore (kısa)
                                    };

                                    string resolvedUrl = await ResolveFirstExistingAsync(candidates);
                                    
                                    return new UpdateInfo
                                    {
                                        HasUpdate = hasUpdate,
                                        CurrentVersion = CurrentVersion,
                                        LatestVersion = latestVersion,
                                        DownloadUrl = resolvedUrl ?? candidates[0],
                                        ReleaseNotes = ""
                                    };
                                }
                            }
                        }
                    }
                    
                    ErrorLogger.LogWarning($"Beklenmeyen yanıt: {response.StatusCode}");
                    return new UpdateInfo { HasUpdate = false };
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Güncelleme kontrolü hatası", ex);
                return new UpdateInfo 
                { 
                    HasUpdate = false, 
                    ErrorMessage = ex.Message 
                };
            }
        }

        /// <summary>
        /// Versiyon string'ini normalize et (2.0 -> 2.0.0.0)
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            var parts = version.Split('.');
            while (parts.Length < 4)
            {
                version += ".0";
                parts = version.Split('.');
            }
            return string.Join(".", parts);
        }

        /// <summary>
        /// Versiyon string'ini kısalt (4.0.0 -> 4.0) - Inno Setup dosya adı için
        /// </summary>
        /// <summary>
        /// Versiyon string'ini kısalt - Inno Setup dosya adı için
        /// 4.0.0 -> 4.0 (son 0'ları kaldır), 4.0.6 -> 4.0.6 (olduğu gibi)
        /// </summary>
        private static string GetShortVersion(string version)
        {
            var parts = version.Split('.');
            
            // Son 0'ları kaldır
            while (parts.Length > 2 && parts[parts.Length - 1] == "0")
            {
                Array.Resize(ref parts, parts.Length - 1);
            }
            
            return string.Join(".", parts);
        }

        /// <summary>
        /// Güncelleme varsa kullanıcıya göster ve otomatik indir/kur
        /// </summary>
        public static async Task CheckAndPromptAsync(bool silentIfNoUpdate = true)
        {
            var updateInfo = await CheckForUpdatesAsync();

            if (updateInfo.HasUpdate)
            {
                var result = ShowUpdateDialog(updateInfo);
                
                if (result == true)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            else if (!silentIfNoUpdate)
            {
                if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
                {
                    ShowInfoDialog("Güncelleme Kontrolü", $"Güncelleme kontrolü yapılamadı:\n{updateInfo.ErrorMessage}", isError: true);
                }
                else
                {
                    ShowInfoDialog("Güncelleme Kontrolü", $"En güncel sürümü kullanıyorsunuz.\n\nMevcut sürüm: v{CurrentVersion}", isError: false);
                }
            }
        }

        /// <summary>
        /// Güncelleme bilgisi popup'ı göster (dışarıdan çağrılabilir)
        /// </summary>
        public static async Task ShowUpdatePopupAsync()
        {
            var updateInfo = await CheckForUpdatesAsync();

            if (updateInfo.HasUpdate)
            {
                var result = ShowUpdateDialog(updateInfo);
                
                if (result == true)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
                {
                    ShowInfoDialog("Güncelleme Kontrolü", $"Güncelleme kontrolü yapılamadı:\n{updateInfo.ErrorMessage}", isError: true);
                }
                else
                {
                    ShowInfoDialog("Yazılım Güncel", $"En güncel sürümü kullanıyorsunuz.\n\nMevcut sürüm: v{CurrentVersion}", isError: false);
                }
            }
        }

        /// <summary>
        /// Programın stiline uygun güncelleme dialog'u
        /// </summary>
        private static bool? ShowUpdateDialog(UpdateInfo updateInfo)
        {
            var dialog = new Window
            {
                Title = "Güncelleme Mevcut",
                Width = 420,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8)
            };

            var mainStack = new StackPanel { Margin = new Thickness(20) };

            // Başlık
            var titleText = new TextBlock
            {
                Text = "🔄 Yeni Güncelleme Mevcut!",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            // Versiyon bilgisi
            var versionGrid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            versionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            versionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            versionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            versionGrid.RowDefinitions.Add(new RowDefinition());

            var currentVer = new TextBlock
            {
                Text = $"v{updateInfo.CurrentVersion}",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(currentVer, 0);

            var arrow = new TextBlock
            {
                Text = "➜",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(15, 0, 15, 0)
            };
            Grid.SetColumn(arrow, 1);

            var newVer = new TextBlock
            {
                Text = $"v{updateInfo.LatestVersion}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(newVer, 2);

            versionGrid.Children.Add(currentVer);
            versionGrid.Children.Add(arrow);
            versionGrid.Children.Add(newVer);

            // Açıklama
            var descText = new TextBlock
            {
                Text = "Güncelleme otomatik olarak indirilip kurulacak.\nProgram yeniden başlatılacak.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 25)
            };

            // Butonlar
            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var yesButton = CreateStyledButton("✓ Güncelle", Color.FromRgb(76, 175, 80), Color.FromRgb(102, 187, 106));
            yesButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };

            var noButton = CreateStyledButton("✕ Şimdi Değil", Color.FromRgb(120, 120, 120), Color.FromRgb(150, 150, 150));
            noButton.Margin = new Thickness(15, 0, 0, 0);
            noButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };

            buttonStack.Children.Add(yesButton);
            buttonStack.Children.Add(noButton);

            mainStack.Children.Add(titleText);
            mainStack.Children.Add(versionGrid);
            mainStack.Children.Add(descText);
            mainStack.Children.Add(buttonStack);

            border.Child = mainStack;
            dialog.Content = border;

            // Pencereyi sürüklenebilir yap
            border.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) dialog.DragMove(); };

            return dialog.ShowDialog();
        }

        /// <summary>
        /// Bilgi dialog'u (güncel/hata durumu için)
        /// </summary>
        private static void ShowInfoDialog(string title, string message, bool isError)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 380,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            var accentColor = isError ? Color.FromRgb(244, 67, 54) : Color.FromRgb(76, 175, 80);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8)
            };

            var mainStack = new StackPanel { Margin = new Thickness(20) };

            var titleText = new TextBlock
            {
                Text = isError ? "⚠️ " + title : "✓ " + title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var msgText = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };

            var okButton = CreateStyledButton("Tamam", accentColor, isError ? Color.FromRgb(255, 100, 100) : Color.FromRgb(102, 187, 106));
            okButton.HorizontalAlignment = HorizontalAlignment.Center;
            okButton.Click += (s, e) => dialog.Close();

            mainStack.Children.Add(titleText);
            mainStack.Children.Add(msgText);
            mainStack.Children.Add(okButton);

            border.Child = mainStack;
            dialog.Content = border;

            border.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) dialog.DragMove(); };

            dialog.ShowDialog();
        }

        /// <summary>
        /// Stilize edilmiş buton oluştur
        /// </summary>
        private static Button CreateStyledButton(string content, Color bgColor, Color hoverColor)
        {
            var button = new Button
            {
                Content = content,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(bgColor),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(20, 10, 20, 10),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;
            button.Template = template;

            button.MouseEnter += (s, e) => button.Background = new SolidColorBrush(hoverColor);
            button.MouseLeave += (s, e) => button.Background = new SolidColorBrush(bgColor);

            return button;
        }

        /// <summary>
        /// Güncellemeyi indir, kur ve programı yeniden başlat
        /// </summary>
        private static async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), TEMP_INSTALLER_NAME);
            
            Window progressWindow = null;
            try
            {
                // Progress dialog göster - programın stiline uygun
                progressWindow = new Window
                {
                    Title = "Güncelleme İndiriliyor",
                    Width = 400,
                    Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true
                };

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(8)
                };

                var mainStack = new StackPanel
                {
                    Margin = new Thickness(25),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var titleText = new TextBlock
                {
                    Text = $"⬇️ v{updateInfo.LatestVersion} indiriliyor...",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                var progressBar = new ProgressBar
                {
                    Height = 8,
                    IsIndeterminate = true,
                    Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    BorderThickness = new Thickness(0)
                };

                var infoText = new TextBlock
                {
                    Text = "Lütfen bekleyin...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                mainStack.Children.Add(titleText);
                mainStack.Children.Add(progressBar);
                mainStack.Children.Add(infoText);

                border.Child = mainStack;
                progressWindow.Content = border;

                // Sürüklenebilir
                border.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) progressWindow.DragMove(); };

                progressWindow.Show();
                
                // İndirme işlemi
                ErrorLogger.LogInfo($"Güncelleme indiriliyor: {updateInfo.DownloadUrl}");
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "RaptorexController-Updater");
                    client.Timeout = TimeSpan.FromMinutes(5);
                    
                    var response = await client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
                
                progressWindow.Close();
                
                // Kurulum başlat ve programı kapat
                StartInstallerAndExit(tempPath);
            }
            catch (Exception ex)
            {
                progressWindow?.Close();
                ErrorLogger.LogError($"Güncelleme indirme hatası - URL: {updateInfo.DownloadUrl}", ex);
                ShowInfoDialog("İndirme Hatası", $"Güncelleme indirilemedi:\n{ex.Message}\n\nURL: {updateInfo.DownloadUrl}\n\nManuel olarak GitHub'dan indirebilirsiniz.", isError: true);
                
                // Temp dosyasını temizle
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// Installer'ı başlat ve programı kapat
        /// </summary>
        private static void StartInstallerAndExit(string installerPath)
        {
            try
            {
                // Batch dosyası oluştur: bekle, sonra installer'ı çalıştır
                string batchPath = Path.Combine(Path.GetTempPath(), "RaptorexUpdate.bat");
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string exeName = Path.GetFileNameWithoutExtension(exePath);
                
                // Batch script: uygulamanın kapanmasını bekle, sonra installer'ı çalıştır
                string batchContent = $@"@echo off
:waitloop
tasklist /FI ""IMAGENAME eq {exeName}.exe"" 2>NUL | find /I ""{exeName}.exe"" >NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >NUL
    goto waitloop
)
start """" ""{installerPath}"" /SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS
del ""%~f0""
";
                File.WriteAllText(batchPath, batchContent);
                
                // Batch'i gizli pencerede başlat
                var startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                
                Process.Start(startInfo);
                
                // Uygulamayı kapat
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Installer başlatma hatası", ex);
                ShowInfoDialog("Kurulum Hatası", $"Kurulum başlatılamadı:\n{ex.Message}\n\nLütfen indirilen dosyayı manuel çalıştırın:\n{installerPath}", isError: true);
            }
        }

        /// <summary>
        /// Verilen URL adaylarından ilk erişilebilenini döndürür (HEAD/redirect kontrolü)
        /// </summary>
        private static async Task<string> ResolveFirstExistingAsync(string[] urls)
        {
            foreach (var url in urls)
            {
                try
                {
                    using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                    using (var client = new HttpClient(handler))
                    using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "RaptorexController-UpdateChecker");
                        client.Timeout = TimeSpan.FromSeconds(8);
                        var response = await client.SendAsync(request);
                        if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 400)
                        {
                            return url;
                        }
                    }
                }
                catch
                {
                    // ignore and try next
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Güncelleme bilgisi
    /// </summary>
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public string ErrorMessage { get; set; }
    }
}
