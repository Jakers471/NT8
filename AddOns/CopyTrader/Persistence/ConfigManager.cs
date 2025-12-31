// ConfigManager.cs
// Handles persistence of CopyTrader configuration

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.CopyTrader
{
    public static class ConfigManager
    {
        private static readonly string DataFolder;
        private static readonly string ConfigFile;
        private static readonly string LogFile;
        private static readonly object _lock = new object();

        private static List<CopyLogEntry> _logEntries = new List<CopyLogEntry>();
        private const int MAX_LOG_ENTRIES = 200;

        static ConfigManager()
        {
            DataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "CopyTrader");

            ConfigFile = Path.Combine(DataFolder, "config.xml");
            LogFile = Path.Combine(DataFolder, "log.xml");

            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        public static void SaveConfig(CopyTraderConfig config)
        {
            lock (_lock)
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(CopyTraderConfig));
                    using (var writer = new StreamWriter(ConfigFile))
                    {
                        serializer.Serialize(writer, config);
                    }
                    Log("Configuration saved");
                }
                catch (Exception ex)
                {
                    Log($"Error saving config: {ex.Message}");
                }
            }
        }

        public static CopyTraderConfig LoadConfig()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(ConfigFile))
                        return new CopyTraderConfig();

                    var serializer = new XmlSerializer(typeof(CopyTraderConfig));
                    using (var reader = new StreamReader(ConfigFile))
                    {
                        return (CopyTraderConfig)serializer.Deserialize(reader);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading config: {ex.Message}");
                    return new CopyTraderConfig();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ACTIVITY LOG
        // ═══════════════════════════════════════════════════════════════

        public static void AddLogEntry(CopyLogEntry entry)
        {
            lock (_lock)
            {
                _logEntries.Insert(0, entry);
                if (_logEntries.Count > MAX_LOG_ENTRIES)
                    _logEntries.RemoveRange(MAX_LOG_ENTRIES, _logEntries.Count - MAX_LOG_ENTRIES);

                SaveLog();
            }
        }

        public static List<CopyLogEntry> GetLogEntries()
        {
            lock (_lock)
            {
                return new List<CopyLogEntry>(_logEntries);
            }
        }

        public static void ClearLog()
        {
            lock (_lock)
            {
                _logEntries.Clear();
                try
                {
                    if (File.Exists(LogFile))
                        File.Delete(LogFile);
                }
                catch { }
            }
        }

        public static void LoadLog()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(LogFile))
                        return;

                    var serializer = new XmlSerializer(typeof(CopyLogStorage));
                    using (var reader = new StreamReader(LogFile))
                    {
                        var storage = (CopyLogStorage)serializer.Deserialize(reader);
                        _logEntries = storage.Entries ?? new List<CopyLogEntry>();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading log: {ex.Message}");
                    _logEntries = new List<CopyLogEntry>();
                }
            }
        }

        private static void SaveLog()
        {
            try
            {
                var storage = new CopyLogStorage { Entries = _logEntries };
                var serializer = new XmlSerializer(typeof(CopyLogStorage));
                using (var writer = new StreamWriter(LogFile))
                {
                    serializer.Serialize(writer, storage);
                }
            }
            catch { }
        }

        private static void Log(string message)
        {
            Output.Process($"[CopyTrader] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }
    }
}
