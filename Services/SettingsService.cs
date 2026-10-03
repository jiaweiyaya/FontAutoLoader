using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FontAutoLoader.Services
{
    public class AppSettings
    {
        public bool AutoStart { get; set; } = false;
        public bool SilentStart { get; set; } = false;
        public bool AllowDeleteInstalled { get; set; } = true;
        public bool AllowDeleteWarning { get; set; } = false;
        public bool AllowDeleteCritical { get; set; } = false;
        public int CloseAction { get; set; } = 0; // 0: 弹窗询问, 1: 彻底退出, 2: 最小化到托盘
    }

    public static class SettingsService
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FontAutoLoader");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FontAutoLoader";

        public static AppSettings Current { get; private set; } = new();

        static SettingsService()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        Current = loaded;
                        return;
                    }
                }
            }
            catch
            {
            }
            Current = new AppSettings();
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
            }
        }

        public static void ApplyAutoStartRegistry(bool autoStart, bool silentStart)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (autoStart)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        string cmd = $"\"{exePath}\"" + (silentStart ? " --silent" : "");
                        key.SetValue(AppName, cmd);
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch
            {
            }
        }
    }
}