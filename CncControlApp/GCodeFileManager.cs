using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using IoPath = System.IO.Path;

namespace CncControlApp
{
    /// <summary>
    /// Handles G-code file operations only
    /// </summary>
    public class GCodeFileManager
    {
        /// <summary>
        /// Load G-code file - file processing only
        /// </summary>
        public void LoadGCodeFile(string filePath, ObservableCollection<GCodeLineItem> gcodeLines,
            List<GCodeSegment> gcodeSegments, GCodeParser parser)
        {
            try
            {
                // Clear existing data
                Application.Current.Dispatcher.Invoke(() =>
                {
                    gcodeLines.Clear();
                    gcodeSegments.Clear();
                });

                // Process file
                ProcessFileData(filePath, gcodeLines, gcodeSegments, parser);
            }
            catch (Exception ex)
            {
                // Simple error message - no fancy logging
                System.Diagnostics.Debug.WriteLine($"Error loading G-code file: {ex.Message}");
            }
        }

        /// <summary>
        /// Process file data
        /// </summary>
        private void ProcessFileData(string filePath, ObservableCollection<GCodeLineItem> gcodeLines,
            List<GCodeSegment> gcodeSegments, GCodeParser parser)
        {
            if (!File.Exists(filePath)) return;

            string[] lines = File.ReadAllLines(filePath);
            var lineItems = new List<GCodeLineItem>();

            parser.Reset();

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmedLine = lines[i].Trim();
                int lineNumber = i + 1;

                if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    lineItems.Add(new GCodeLineItem
                    {
                        LineNumber = lineNumber,
                        GCodeLine = trimmedLine
                    });

                    parser.ParseGCodeLine(trimmedLine, lineNumber, gcodeSegments);
                }
            }

            // Add to UI collection
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var lineItem in lineItems)
                {
                    gcodeLines.Add(lineItem);
                }
            });
        }
    }
}