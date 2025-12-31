// StateManager.cs
// Handles persistence of lockout state and config to survive restarts

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Manages persistence of Risk Manager state and configuration.
    /// Saves to XML files in NinjaTrader data directory.
    /// </summary>
    public static class StateManager
    {
        private static readonly string DataFolder;
        private static readonly string LockoutFile;
        private static readonly string ConfigFile;
        private static readonly object _lock = new object();

        static StateManager()
        {
            // Use NT8's data folder for persistence
            DataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "RiskManager");

            LockoutFile = Path.Combine(DataFolder, "lockouts.xml");
            ConfigFile = Path.Combine(DataFolder, "config.xml");

            // Ensure folder exists
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        // ═══════════════════════════════════════════════════════════════
        // LOCKOUT STATE PERSISTENCE
        // ═══════════════════════════════════════════════════════════════

        public static void SaveLockouts(Dictionary<string, LockoutData> lockouts)
        {
            lock (_lock)
            {
                try
                {
                    var data = new LockoutStorage { Lockouts = new List<LockoutData>(lockouts.Values) };
                    var serializer = new XmlSerializer(typeof(LockoutStorage));
                    using (var writer = new StreamWriter(LockoutFile))
                    {
                        serializer.Serialize(writer, data);
                    }
                    Log($"Saved {lockouts.Count} lockout(s) to disk");
                }
                catch (Exception ex)
                {
                    Log($"Error saving lockouts: {ex.Message}");
                }
            }
        }

        public static Dictionary<string, LockoutData> LoadLockouts()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, LockoutData>();
                try
                {
                    if (!File.Exists(LockoutFile))
                        return result;

                    var serializer = new XmlSerializer(typeof(LockoutStorage));
                    using (var reader = new StreamReader(LockoutFile))
                    {
                        var data = (LockoutStorage)serializer.Deserialize(reader);
                        foreach (var lockout in data.Lockouts)
                        {
                            // Check if lockout is still valid
                            if (lockout.Type == LockoutDuration.Timed && DateTime.Now >= lockout.ExpiresAt)
                            {
                                Log($"Expired lockout for {lockout.AccountName} - skipping");
                                continue;
                            }

                            // Check if it's a new day for daily lockouts
                            if (lockout.Type == LockoutDuration.UntilReset)
                            {
                                // If lockout started before today's reset time, clear it
                                var today = DateTime.Today;
                                var resetTime = today.Add(lockout.ResetTime);
                                if (DateTime.Now >= resetTime && lockout.StartedAt < resetTime)
                                {
                                    Log($"Daily reset passed for {lockout.AccountName} - skipping");
                                    continue;
                                }
                            }

                            result[lockout.AccountName] = lockout;
                            Log($"Loaded active lockout for {lockout.AccountName}: {lockout.Reason}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading lockouts: {ex.Message}");
                }
                return result;
            }
        }

        public static void ClearLockouts()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(LockoutFile))
                        File.Delete(LockoutFile);
                    Log("Lockout file cleared");
                }
                catch (Exception ex)
                {
                    Log($"Error clearing lockouts: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION PERSISTENCE
        // ═══════════════════════════════════════════════════════════════

        public static void SaveConfig(RiskConfig config)
        {
            lock (_lock)
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(RiskConfig));
                    using (var writer = new StreamWriter(ConfigFile))
                    {
                        serializer.Serialize(writer, config);
                    }
                    Log("Configuration saved to disk");
                }
                catch (Exception ex)
                {
                    Log($"Error saving config: {ex.Message}");
                }
            }
        }

        public static RiskConfig LoadConfig()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(ConfigFile))
                        return new RiskConfig(); // Return defaults

                    var serializer = new XmlSerializer(typeof(RiskConfig));
                    using (var reader = new StreamReader(ConfigFile))
                    {
                        return (RiskConfig)serializer.Deserialize(reader);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading config: {ex.Message}");
                    return new RiskConfig(); // Return defaults on error
                }
            }
        }

        private static void Log(string message)
        {
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SERIALIZABLE DATA CLASSES
    // ═══════════════════════════════════════════════════════════════

    [Serializable]
    public class LockoutStorage
    {
        public List<LockoutData> Lockouts { get; set; } = new List<LockoutData>();
    }

    [Serializable]
    public class LockoutData
    {
        public string AccountName { get; set; }
        public bool IsLocked { get; set; }
        public LockoutDuration Type { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public TimeSpan ResetTime { get; set; } = new TimeSpan(18, 0, 0);
        public string Reason { get; set; }
    }

    [Serializable]
    public class RiskConfig
    {
        // Total Daily Loss Rule
        public bool TotalLossEnabled { get; set; } = true;
        public double TotalLossMax { get; set; } = 500;
        public int TotalLossResetHour { get; set; } = 18;

        // Unrealized Loss Rule (Floating Stop)
        public bool FloatingStopEnabled { get; set; } = true;
        public double FloatingStopMax { get; set; } = 150;

        // Unrealized Profit Rule (Take Profit)
        public bool TakeProfitEnabled { get; set; } = false;
        public double TakeProfitTarget { get; set; } = 300;

        // Realized Loss Rule
        public bool RealizedLossEnabled { get; set; } = false;
        public double RealizedLossMax { get; set; } = 400;
        public int RealizedLossResetHour { get; set; } = 18;

        // Global settings
        public bool SoundEnabled { get; set; } = true;
    }
}
