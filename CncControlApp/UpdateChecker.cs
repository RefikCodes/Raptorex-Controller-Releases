using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

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
                ErrorLogger.LogInfo($"Güncelleme kontrolü başlatılıyor... Mevcut versiyon: {CurrentVersion}");
                
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
                        ErrorLogger.LogInfo($"Redirect URL: {redirectUrl}");
                        
                        if (!string.IsNullOrEmpty(redirectUrl))
                        {
                            // URL'den tag'i çıkar: .../releases/tag/v2.0.6 -> v2.0.6
                            var match = Regex.Match(redirectUrl, @"/tag/v?([0-9.]+)");
                            if (match.Success)
                            {
                                string latestVersionStr = match.Groups[1].Value;
                                ErrorLogger.LogInfo($"Bulunan versiyon: {latestVersionStr}");
                                
                                if (Version.TryParse(NormalizeVersion(latestVersionStr), out Version latestVersion))
                                {
                                    bool hasUpdate = latestVersion > CurrentVersion;
                                    ErrorLogger.LogInfo($"Karşılaştırma: {latestVersion} > {CurrentVersion} = {hasUpdate}");
                                    
                                    // Download URL'i oluştur
                                    string downloadUrl = $"https://github.com/{GITHUB_REPO}/releases/download/v{latestVersionStr}/RaptorexController_Setup_{latestVersionStr}.exe";
                                    
                                    return new UpdateInfo
                                    {
                                        HasUpdate = hasUpdate,
                                        CurrentVersion = CurrentVersion,
                                        LatestVersion = latestVersion,
                                        DownloadUrl = downloadUrl,
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
        /// Güncelleme varsa kullanıcıya göster ve otomatik indir/kur
        /// </summary>
        public static async Task CheckAndPromptAsync(bool silentIfNoUpdate = true)
        {
            var updateInfo = await CheckForUpdatesAsync();

            if (updateInfo.HasUpdate)
            {
                string message = $"Yeni sürüm mevcut!\n\n" +
                                 $"Mevcut: v{updateInfo.CurrentVersion}\n" +
                                 $"Yeni: v{updateInfo.LatestVersion}\n\n" +
                                 $"Güncelleme otomatik olarak indirilip kurulacak.\nProgram yeniden başlatılacak.\n\n" +
                                 $"Devam edilsin mi?";

                var result = MessageBox.Show(message, "Güncelleme Mevcut", 
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            else if (!silentIfNoUpdate)
            {
                if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
                {
                    MessageBox.Show($"Güncelleme kontrolü yapılamadı:\n{updateInfo.ErrorMessage}", 
                        "Güncelleme Kontrolü", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"En güncel sürümü kullanıyorsunuz.\nMevcut sürüm: v{CurrentVersion}", 
                        "Güncelleme Kontrolü", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// Güncellemeyi indir, kur ve programı yeniden başlat
        /// </summary>
        private static async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), TEMP_INSTALLER_NAME);
            
            try
            {
                ErrorLogger.LogInfo($"Güncelleme indiriliyor: {updateInfo.DownloadUrl}");
                
                // Progress dialog göster
                var progressWindow = new Window
                {
                    Title = "Güncelleme İndiriliyor",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow
                };
                
                var stackPanel = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(20),
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                var statusText = new System.Windows.Controls.TextBlock
                {
                    Text = $"v{updateInfo.LatestVersion} indiriliyor...",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                var progressBar = new System.Windows.Controls.ProgressBar
                {
                    Height = 25,
                    IsIndeterminate = true
                };
                
                stackPanel.Children.Add(statusText);
                stackPanel.Children.Add(progressBar);
                progressWindow.Content = stackPanel;
                
                progressWindow.Show();
                
                // İndirme işlemi
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
                
                ErrorLogger.LogInfo($"Güncelleme indirildi: {tempPath}");
                
                // Kurulum başlat ve programı kapat
                StartInstallerAndExit(tempPath);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Güncelleme indirme hatası", ex);
                MessageBox.Show($"Güncelleme indirilemedi:\n{ex.Message}\n\nManuel olarak GitHub'dan indirebilirsiniz.", 
                    "İndirme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                
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
                ErrorLogger.LogInfo($"Installer başlatılıyor: {installerPath}");
                
                // Installer'ı silent modda başlat (/SILENT veya /VERYSILENT Inno Setup için)
                // Program kapandıktan sonra kurulum başlayacak
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                };
                
                Process.Start(startInfo);
                
                ErrorLogger.LogInfo("Installer başlatıldı, program kapatılıyor...");
                
                // Uygulamayı kapat
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Installer başlatma hatası", ex);
                MessageBox.Show($"Kurulum başlatılamadı:\n{ex.Message}\n\nLütfen indirilen dosyayı manuel çalıştırın:\n{installerPath}", 
                    "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
