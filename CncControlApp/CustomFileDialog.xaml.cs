using CncControlApp.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Input;

namespace CncControlApp
{
    public partial class CustomFileDialog : Window
    {
        public string SelectedFile { get; private set; }
        public bool DialogResult { get; private set; }

        private ObservableCollection<LocationItem> locations;
        private bool isLoading = false;
        private DateTime lastTouchTime = DateTime.MinValue;
        private FileItem lastTouchedItem = null;
        private Point touchStartPosition;
        private bool isTouchScrolling = false;

        public CustomFileDialog()
        {
            InitializeComponent();

            // Lokasyonları başlat ve ilk dizini yükle
            InitializeLocations();
            LoadInitialDirectory();

            // Diskler arka planda yüklenecek
            _ = LoadDrivesAsync();
        }

        private void InitializeLocations()
        {
            locations = new ObservableCollection<LocationItem>();

            try
            {
                // Temel sistem klasörleri - alfabetik sırala
                var systemLocations = new List<LocationItem>();

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktop))
                    systemLocations.Add(new LocationItem { Name = "Masaüstü", Icon = "🖥️", Path = desktop });

                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (Directory.Exists(documents))
                    systemLocations.Add(new LocationItem { Name = "Belgeler", Icon = "📄", Path = documents });

                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloads))
                    systemLocations.Add(new LocationItem { Name = "İndirilenler", Icon = "⬇️", Path = downloads });

                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (Directory.Exists(pictures))
                    systemLocations.Add(new LocationItem { Name = "Resimler", Icon = "🖼️", Path = pictures });

                // G-Code ile alakalı özel klasörler
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] potentialGCodeFolders = {
                    Path.Combine(userProfile, "CNC"),
                    Path.Combine(userProfile, "GCode"),
                    Path.Combine(userProfile, "CAM"),
                    Path.Combine(userProfile, "Projects"),
                    Path.Combine(documents, "CNC"),
                    Path.Combine(documents, "GCode"),
                    Path.Combine(documents, "CAM"),
                    Path.Combine(documents, "Projects")
                };

                foreach (string folder in potentialGCodeFolders)
                {
                    if (Directory.Exists(folder))
                    {
                        systemLocations.Add(new LocationItem 
                        { 
                            Name = Path.GetFileName(folder), 
                            Icon = "⚙️", 
                            Path = folder 
                        });
                    }
                }

                // Bu PC'yi en üste ekle
                locations.Add(new LocationItem { Name = "Bu Bilgisayar", Icon = "💻", Path = "ThisPC" });

                // Separator
                locations.Add(new LocationItem { Name = "────────────", Icon = "", Path = "SEPARATOR" });

                // Alfabetik sıralı sistem klasörleri
                var sortedLocations = systemLocations.OrderBy(l => l.Name).ToList();
                foreach (var location in sortedLocations)
                {
                    locations.Add(location);
                }

                // Diskler için placeholder
                locations.Add(new LocationItem { Name = "────────────", Icon = "", Path = "SEPARATOR" });
                locations.Add(new LocationItem { Name = "🔄 Diskler yükleniyor...", Icon = "⏳", Path = "LOADING" });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lokasyonlar yüklenirken hata: {ex.Message}");
            }

            LocationListBox.ItemsSource = locations;
        }

        private async Task LoadDrivesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Loading indicator'ı kaldır
                    Dispatcher.Invoke(() =>
                    {
                        var loadingItem = locations.FirstOrDefault(x => x.Path == "LOADING");
                        if (loadingItem != null)
                        {
                            locations.Remove(loadingItem);
                        }
                    });

                    // Diskler - timeout ile güvenli yükleme
                    var drivesTask = Task.Run(() => DriveInfo.GetDrives());
                    if (drivesTask.Wait(5000)) //5 saniye timeout
                    {
                        var drives = drivesTask.Result;
                        var sortedDrives = drives.Where(d => d.IsReady).OrderBy(d => d.Name).ToList();
                        
                        foreach (var drive in sortedDrives)
                        {
                            try
                            {
                                var driveCheckTask = Task.Run(() =>
                                {
                                    return new
                                    {
                                        Name = string.IsNullOrEmpty(drive.VolumeLabel)
                                            ? $"Disk ({drive.Name.TrimEnd('\\', '/')})"
                                            : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\', '/')})",
                                        Path = drive.RootDirectory.FullName,
                                        Icon = drive.DriveType == DriveType.Removable ? "💾" : 
                                               drive.DriveType == DriveType.Network ? "🌐" : "💽"
                                    };
                                });

                                if (driveCheckTask.Wait(2000)) // Her disk için2 saniye timeout
                                {
                                    var driveInfo = driveCheckTask.Result;
                                    Dispatcher.Invoke(() =>
                                    {
                                        locations.Add(new LocationItem
                                        {
                                            Name = driveInfo.Name,
                                            Icon = driveInfo.Icon,
                                            Path = driveInfo.Path
                                        });
                                    });
                                }
                            }
                            catch (Exception)
                            {
                                // Tek disk hatası diğerlerini etkilemesin
                                Dispatcher.Invoke(() =>
                                {
                                    locations.Add(new LocationItem
                                    {
                                        Name = $"❌ {drive.Name}",
                                        Icon = "⚠️",
                                        Path = ""
                                    });
                                });
                            }
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            locations.Add(new LocationItem
                            {
                                Name = "⚠️ Diskler yüklenemedi",
                                Icon = "⏰",
                                Path = ""
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        locations.Add(new LocationItem
                        {
                            Name = $"❌ Disk hatası",
                            Icon = "⚠️",
                            Path = ""
                        });
                    });
                }
            });
        }

        private async void LoadInitialDirectory()
        {
            try
            {
                // Prefer persisted last directory
                var lastDir = UserPreferences.LoadLastDirectory();
                string startDir = !string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir)
                    ? lastDir
                    : Directory.GetCurrentDirectory();

                CurrentPathTextBlock.Text = startDir;
                await LoadDirectoryAsync(startDir);

                // Location listesinde mevcut dizini seç
                var currentLocation = locations.FirstOrDefault(l => l.Path == startDir);
                if (currentLocation != null)
                {
                    LocationListBox.SelectedItem = currentLocation;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Başlangıç dizini yüklenirken hata: {ex.Message}");
            }
        }

        private async Task LoadDirectoryAsync(string path)
        {
            if (isLoading || string.IsNullOrEmpty(path) || 
                path == "LOADING" || path == "SEPARATOR") 
                return;

            isLoading = true;

            try
            {
                // Loading indicator göster
                FileListBox.Items.Clear();
                FileListBox.Items.Add(new FileItem
                {
                    Name = "🔄 Yükleniyor...",
                    IsDirectory = false,
                    FullPath = ""
                });

                if (path == "ThisPC")
                {
                    await LoadThisPCViewAsync();
                    return;
                }

                // Dosya sistemi işlemlerini arka planda yap
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        FileListBox.Items.Clear();
                        CurrentPathTextBlock.Text = path;
                        // Persist last directory on every successful navigation
                        try { UserPreferences.SaveLastDirectory(path); } catch { }
                    });

                    try
                    {
                        // Parent directory
                        var parentDir = Directory.GetParent(path);
                        if (parentDir != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                FileListBox.Items.Add(new FileItem
                                {
                                    Name = "..",
                                    IsDirectory = true,
                                    FullPath = parentDir.FullName
                                });
                            });
                        }

                        // Directories - alfabetik sırala
                        var directoriesTask = Task.Run(() => Directory.GetDirectories(path)
                            .OrderBy(d => Path.GetFileName(d))
                            .Take(100)
                            .ToArray());
                        
                        if (directoriesTask.Wait(3000))
                        {
                            var directories = directoriesTask.Result;
                            foreach (var dir in directories)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    FileListBox.Items.Add(new FileItem
                                    {
                                        Name = Path.GetFileName(dir),
                                        IsDirectory = true,
                                        FullPath = dir
                                    });
                                });
                            }
                        }

                        // Files - alfabetik sırala
                        var filesTask = Task.Run(() => Directory.GetFiles(path)
                            .Where(f => f.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".nc", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".cnc", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => Path.GetFileName(f))
                            .Take(200)
                            .ToArray());

                        if (filesTask.Wait(3000))
                        {
                            var files = filesTask.Result;
                            foreach (var file in files)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    FileListBox.Items.Add(new FileItem
                                    {
                                        Name = Path.GetFileName(file),
                                        IsDirectory = false,
                                        FullPath = file
                                    });
                                });
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FileListBox.Items.Clear();
                            FileListBox.Items.Add(new FileItem
                            {
                                Name = "❌ Bu klasöre erişim izniniz yok",
                                IsDirectory = false,
                                FullPath = ""
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FileListBox.Items.Clear();
                            FileListBox.Items.Add(new FileItem
                            {
                                Name = $"❌ Hata: {ex.Message}",
                                IsDirectory = false,
                                FullPath = ""
                            });
                        });
                    }
                });
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task LoadThisPCViewAsync()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    FileListBox.Items.Clear();
                    CurrentPathTextBlock.Text = "Bu Bilgisayar";
                });

                try
                {
                    // Mevcut diskler listesini göster
                    var diskItems = locations.Where(l => l.Icon == "💽" || l.Icon == "💾" || l.Icon == "🌐")
                                            .OrderBy(l => l.Name)
                                            .ToList();
                    
                    foreach (var disk in diskItems)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FileListBox.Items.Add(new FileItem
                            {
                                Name = disk.Name,
                                IsDirectory = true,
                                FullPath = disk.Path
                            });
                        });
                    }

                    if (!diskItems.Any())
                    {
                        // Diskler henüz yüklenmediyse direkt yükleme
                        var drivesTask = Task.Run(() => DriveInfo.GetDrives());
                        if (drivesTask.Wait(3000))
                        {
                            var drives = drivesTask.Result.Where(d => d.IsReady)
                                                         .OrderBy(d => d.Name)
                                                         .Take(10);
                            foreach (var drive in drives)
                            {
                                try
                                {
                                    string driveName = string.IsNullOrEmpty(drive.VolumeLabel)
                                        ? $"Disk ({drive.Name.TrimEnd('\\', '/')})"
                                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\', '/')})";

                                    Dispatcher.Invoke(() =>
                                    {
                                        FileListBox.Items.Add(new FileItem
                                        {
                                            Name = driveName,
                                            IsDirectory = true,
                                            FullPath = drive.RootDirectory.FullName
                                        });
                                    });
                                }
                                catch
                                {
                                    // Hatalı diskleri atla
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        FileListBox.Items.Add(new FileItem
                        {
                            Name = $"❌ Diskler yüklenirken hata: {ex.Message}",
                            IsDirectory = false,
                            FullPath = ""
                        });
                    });
                }
            });
        }

        private async void LocationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocationListBox.SelectedItem is LocationItem selectedLocation && 
                !string.IsNullOrEmpty(selectedLocation.Path))
            {
                await LoadDirectoryAsync(selectedLocation.Path);
            }
        }

        private async void FileListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isLoading) return;

            if (FileListBox.SelectedItem is FileItem item)
            {
                if (item.IsDirectory && !string.IsNullOrEmpty(item.FullPath))
                {
                    await LoadDirectoryAsync(item.FullPath);

                    // Location listesinde ilgili konumu seç
                    var location = locations.FirstOrDefault(l => l.Path == item.FullPath);
                    if (location != null)
                    {
                        LocationListBox.SelectedItem = location;
                    }
                }
                else if (!item.IsDirectory && !string.IsNullOrEmpty(item.FullPath))
                {
                    SelectedFile = item.FullPath;
                    DialogResult = true;
                    // Persist directory of selected file
                    try { UserPreferences.SaveLastDirectory(Path.GetDirectoryName(item.FullPath)); } catch { }
                    Close();
                }
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (FileListBox.SelectedItem is FileItem item && !item.IsDirectory && !string.IsNullOrEmpty(item.FullPath))
            {
                SelectedFile = item.FullPath;
                DialogResult = true;
                try { UserPreferences.SaveLastDirectory(Path.GetDirectoryName(item.FullPath)); } catch { }
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OpenButton.IsEnabled = FileListBox.SelectedItem is FileItem item && 
                                   !item.IsDirectory && 
                                   !string.IsNullOrEmpty(item.FullPath);
        }

        private void FileListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Mouse click handling for better click detection
            var item = GetFileItemFromPoint(e.GetPosition(FileListBox));
            if (item != null)
            {
                FileListBox.SelectedItem = item;
            }
        }

        private void FileListBox_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            touchStartPosition = e.GetTouchPoint(FileListBox).Position;
            isTouchScrolling = false;
            e.Handled = false; // Allow ScrollViewer to handle scrolling
        }

        private async void FileListBox_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (isLoading || isTouchScrolling) return;

            var touchPoint = e.GetTouchPoint(FileListBox).Position;
            
            // Check if this was a scroll gesture (moved more than5 pixels)
            var dx = Math.Abs(touchPoint.X - touchStartPosition.X);
            var dy = Math.Abs(touchPoint.Y - touchStartPosition.Y);
            
            if (dx >5 || dy >5)
            {
                isTouchScrolling = true;
                return; // This was a scroll, not a tap
            }

            var item = GetFileItemFromPoint(touchPoint);
            
            if (item != null)
            {
                FileListBox.SelectedItem = item;
                
                // Double tap detection
                var now = DateTime.Now;
                if (lastTouchedItem == item && (now - lastTouchTime).TotalMilliseconds <500)
                {
                    // Double tap detected
                    if (item.IsDirectory && !string.IsNullOrEmpty(item.FullPath))
                    {
                        await LoadDirectoryAsync(item.FullPath);

                        // Location listesinde ilgili konumu seç
                        var location = locations.FirstOrDefault(l => l.Path == item.FullPath);
                        if (location != null)
                        {
                            LocationListBox.SelectedItem = location;
                        }
                    }
                    else if (!item.IsDirectory && !string.IsNullOrEmpty(item.FullPath))
                    {
                        SelectedFile = item.FullPath;
                        DialogResult = true;
                        try { UserPreferences.SaveLastDirectory(Path.GetDirectoryName(item.FullPath)); } catch { }
                        Close();
                    }
                    
                    lastTouchedItem = null;
                    lastTouchTime = DateTime.MinValue;
                }
                else
                {
                    // First tap
                    lastTouchedItem = item;
                    lastTouchTime = now;
                }
            }
        }

        private FileItem GetFileItemFromPoint(Point point)
        {
            var element = FileListBox.InputHitTest(point) as DependencyObject;
            
            while (element != null && element != FileListBox)
            {
                if (element is ListBoxItem listBoxItem)
                {
                    return listBoxItem.Content as FileItem;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            
            return null;
        }
    }

    public class FileItem
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public string FullPath { get; set; }
        public string Icon => IsDirectory ? "📁" : "📄";
    }

    public class LocationItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Path { get; set; }
    }
}