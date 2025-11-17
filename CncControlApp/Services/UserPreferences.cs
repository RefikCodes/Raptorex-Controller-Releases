using System;
using System.IO;

namespace CncControlApp.Services
{
 /// <summary>
 /// Simple per-user preference storage for small values.
 /// Persists to LocalApplicationData to survive app restarts.
 /// </summary>
 internal static class UserPreferences
 {
 private static readonly string AppFolder = Path.Combine(
 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
 "RaptorexControllerPC");

 private static readonly string LastDirFile = Path.Combine(AppFolder, "lastdir.txt");

 public static string LoadLastDirectory()
 {
 try
 {
 if (File.Exists(LastDirFile))
 {
 var path = File.ReadAllText(LastDirFile).Trim();
 if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
 return path;
 }
 }
 catch { }
 return null;
 }

 public static void SaveLastDirectory(string path)
 {
 try
 {
 if (string.IsNullOrWhiteSpace(path)) return;
 var dir = path;
 if (!Directory.Exists(dir)) return;

 Directory.CreateDirectory(AppFolder);
 File.WriteAllText(LastDirFile, dir);
 }
 catch { }
 }
 }
}