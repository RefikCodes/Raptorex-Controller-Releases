using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace CncControlApp
{
    /// <summary>
    /// GitHub Releases üzerinden otomatik güncelleme kontrolü
    /// </summary>
    public static class UpdateChecker
    {
        private const string GITHUB_REPO = "RefikCodes/Raptorex-Controller-Releases";
        private const string RELEASES_API_URL = "https://api.github.com/repos/{0}/releases/latest";
        private const string RELEASES_PAGE_URL = "https://github.com/{0}/releases/latest";

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
        /// GitHub'dan en son sürümü kontrol eder
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // GitHub API User-Agent gerektirir
                    client.DefaultRequestHeaders.Add("User-Agent", "RaptorexController-UpdateChecker");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    string apiUrl = string.Format(RELEASES_API_URL, GITHUB_REPO);
                    string json = await client.GetStringAsync(apiUrl);

                    // JSON'dan versiyon ve download URL'i çıkar
                    var tagMatch = Regex.Match(json, @"""tag_name""\s*:\s*""v?([^""]+)""");
                    var downloadMatch = Regex.Match(json, @"""browser_download_url""\s*:\s*""([^""]+\.exe)""");
                    var bodyMatch = Regex.Match(json, @"""body""\s*:\s*""([^""]+)""");

                    if (!tagMatch.Success)
                    {
                        return new UpdateInfo { HasUpdate = false };
                    }

                    string latestVersionStr = tagMatch.Groups[1].Value;
                    
                    // Versiyon string'ini temizle (v2.0.0 -> 2.0.0)
                    latestVersionStr = latestVersionStr.TrimStart('v', 'V');
                    
                    if (!Version.TryParse(NormalizeVersion(latestVersionStr), out Version latestVersion))
                    {
                        return new UpdateInfo { HasUpdate = false };
                    }

                    bool hasUpdate = latestVersion > CurrentVersion;

                    return new UpdateInfo
                    {
                        HasUpdate = hasUpdate,
                        CurrentVersion = CurrentVersion,
                        LatestVersion = latestVersion,
                        DownloadUrl = downloadMatch.Success ? downloadMatch.Groups[1].Value : string.Format(RELEASES_PAGE_URL, GITHUB_REPO),
                        ReleaseNotes = bodyMatch.Success ? UnescapeJson(bodyMatch.Groups[1].Value) : ""
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
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
        /// JSON escape karakterlerini çöz
        /// </summary>
        private static string UnescapeJson(string text)
        {
            return text
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"");
        }

        /// <summary>
        /// Güncelleme varsa kullanıcıya göster
        /// </summary>
        public static async Task CheckAndPromptAsync(bool silentIfNoUpdate = true)
        {
            var updateInfo = await CheckForUpdatesAsync();

            if (updateInfo.HasUpdate)
            {
                string message = $"Yeni sürüm mevcut!\n\n" +
                                 $"Mevcut: v{updateInfo.CurrentVersion}\n" +
                                 $"Yeni: v{updateInfo.LatestVersion}\n\n" +
                                 $"{(string.IsNullOrEmpty(updateInfo.ReleaseNotes) ? "" : "Değişiklikler:\n" + updateInfo.ReleaseNotes + "\n\n")}" +
                                 $"İndirmek için GitHub sayfasına gidilsin mi?";

                var result = MessageBox.Show(message, "Güncelleme Mevcut", 
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updateInfo.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Tarayıcı açılamadı: {ex.Message}", "Hata", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
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
