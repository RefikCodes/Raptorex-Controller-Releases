using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CncControlApp.Managers
{
    /// <summary>
    /// Manages probe history records with file persistence
    /// </summary>
    public class ProbeHistoryManager
    {
        private static ProbeHistoryManager _instance;
        public static ProbeHistoryManager Instance => _instance ?? (_instance = new ProbeHistoryManager());

        private readonly string _tempFilePath;
        private int _nextId = 1;

        public ObservableCollection<Models.ProbeRecord> ProbeRecords { get; } = new ObservableCollection<Models.ProbeRecord>();

        public event Action<Models.ProbeRecord> ProbeAdded;

        private ProbeHistoryManager()
        {
            // Use temp folder for probe history
            var tempDir = Path.Combine(Path.GetTempPath(), "RaptorexController");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            _tempFilePath = Path.Combine(tempDir, "probe_history.csv");

            // Load existing records
            LoadFromFile();
        }

        public void AddProbe(string type, double x, double y, double z)
        {
            var record = new Models.ProbeRecord(type, x, y, z)
            {
                Id = _nextId++
            };

            ProbeRecords.Add(record);
            SaveToFile(record);
            ProbeAdded?.Invoke(record);
        }

        public void Clear()
        {
            ProbeRecords.Clear();
            _nextId = 1;

            // Clear file
            try
            {
                if (File.Exists(_tempFilePath))
                {
                    File.WriteAllText(_tempFilePath, "Type,X,Y,Z,Timestamp\n");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing probe history: {ex.Message}");
            }
        }

        public void ExportToCsv(string filePath)
        {
            try
            {
                using (var writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("ID,Type,X,Y,Z,Timestamp");
                    foreach (var record in ProbeRecords)
                    {
                        writer.WriteLine($"{record.Id},{record.Type},{record.X:F3},{record.Y:F3},{record.Z:F3},{record.Timestamp:yyyy-MM-dd HH:mm:ss}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting probe history: {ex.Message}");
                throw;
            }
        }

        private void LoadFromFile()
        {
            try
            {
                if (File.Exists(_tempFilePath))
                {
                    var lines = File.ReadAllLines(_tempFilePath).Skip(1); // Skip header
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var record = Models.ProbeRecord.FromCsvLine(line);
                        if (record != null)
                        {
                            record.Id = _nextId++;
                            ProbeRecords.Add(record);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading probe history: {ex.Message}");
            }
        }

        private void SaveToFile(Models.ProbeRecord record)
        {
            try
            {
                bool fileExists = File.Exists(_tempFilePath);
                using (var writer = new StreamWriter(_tempFilePath, append: true))
                {
                    if (!fileExists)
                    {
                        writer.WriteLine("Type,X,Y,Z,Timestamp");
                    }
                    writer.WriteLine(record.ToString());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving probe record: {ex.Message}");
            }
        }

        public string GetTempFilePath() => _tempFilePath;
    }
}
