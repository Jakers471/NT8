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
        private static readonly string HistoryFile;
        private static readonly string ErrorLogFile;
        private static readonly object _lock = new object();
        private static readonly object _logLock = new object();

        static StateManager()
        {
            // Use NT8's data folder for persistence
            DataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "RiskManager");

            LockoutFile = Path.Combine(DataFolder, "lockouts.xml");
            ConfigFile = Path.Combine(DataFolder, "config.xml");
            HistoryFile = Path.Combine(DataFolder, "closure_history.xml");
            ErrorLogFile = Path.Combine(DataFolder, "error_log.txt");

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

        // ═══════════════════════════════════════════════════════════════
        // CLOSURE HISTORY PERSISTENCE
        // ═══════════════════════════════════════════════════════════════

        private static List<ClosureRecord> _closureHistory = new List<ClosureRecord>();
        private const int MAX_HISTORY_RECORDS = 100;

        public static void RecordClosure(ClosureRecord record)
        {
            lock (_lock)
            {
                _closureHistory.Insert(0, record); // Most recent first

                // Keep only last N records
                if (_closureHistory.Count > MAX_HISTORY_RECORDS)
                    _closureHistory.RemoveRange(MAX_HISTORY_RECORDS, _closureHistory.Count - MAX_HISTORY_RECORDS);

                SaveClosureHistory();
            }
        }

        public static List<ClosureRecord> GetClosureHistory()
        {
            lock (_lock)
            {
                return new List<ClosureRecord>(_closureHistory);
            }
        }

        public static void ClearClosureHistory()
        {
            lock (_lock)
            {
                _closureHistory.Clear();
                try
                {
                    if (File.Exists(HistoryFile))
                        File.Delete(HistoryFile);
                }
                catch { }
            }
        }

        private static void SaveClosureHistory()
        {
            try
            {
                var storage = new ClosureHistoryStorage { Records = _closureHistory };
                var serializer = new XmlSerializer(typeof(ClosureHistoryStorage));
                using (var writer = new StreamWriter(HistoryFile))
                {
                    serializer.Serialize(writer, storage);
                }
            }
            catch (Exception ex)
            {
                Log($"Error saving closure history: {ex.Message}");
            }
        }

        public static void LoadClosureHistory()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(HistoryFile))
                        return;

                    var serializer = new XmlSerializer(typeof(ClosureHistoryStorage));
                    using (var reader = new StreamReader(HistoryFile))
                    {
                        var storage = (ClosureHistoryStorage)serializer.Deserialize(reader);
                        _closureHistory = storage.Records ?? new List<ClosureRecord>();
                    }
                    Log($"Loaded {_closureHistory.Count} closure history records");
                }
                catch (Exception ex)
                {
                    Log($"Error loading closure history: {ex.Message}");
                    _closureHistory = new List<ClosureRecord>();
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CLOSURE HISTORY DATA CLASSES
    // ═══════════════════════════════════════════════════════════════

    [Serializable]
    public class ClosureHistoryStorage
    {
        public List<ClosureRecord> Records { get; set; } = new List<ClosureRecord>();
    }

    [Serializable]
    public class ClosureRecord
    {
        public DateTime Timestamp { get; set; }
        public string Account { get; set; }
        public string Instrument { get; set; }
        public string RuleName { get; set; }
        public string Reason { get; set; }
        public string ActionType { get; set; }  // "FlattenPosition", "FlattenAll", "Lockout"
        public double PnLAtClosure { get; set; }

        public string Summary => $"{Timestamp:HH:mm:ss} | {ActionType} | {Instrument ?? "ALL"} | {RuleName}";
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
        // ═══════════════════════════════════════════════════════════════
        // ACCOUNT-WIDE RULES (Lockout + Flatten ALL)
        // ═══════════════════════════════════════════════════════════════

        // Total Daily Loss (Realized + Unrealized) → LOCKOUT
        public bool TotalLossEnabled { get; set; } = true;
        public double TotalLossMax { get; set; } = 100; // Low for testing
        public int TotalLossResetHour { get; set; } = 18;

        // Daily Realized Loss Only → LOCKOUT
        public bool RealizedLossEnabled { get; set; } = false;
        public double RealizedLossMax { get; set; } = 200;
        public int RealizedLossResetHour { get; set; } = 18;

        // Daily Realized Profit → LOCKOUT (take profit for the day)
        public bool DailyProfitEnabled { get; set; } = false;
        public double DailyProfitTarget { get; set; } = 500;
        public int DailyProfitResetHour { get; set; } = 18;

        // ═══════════════════════════════════════════════════════════════
        // PER-POSITION RULES (Flatten ONLY that position)
        // ═══════════════════════════════════════════════════════════════

        // Unrealized Loss Per Position → Flatten position
        public bool PositionStopEnabled { get; set; } = true;
        public double PositionStopMax { get; set; } = 50; // Low for testing

        // Unrealized Profit Per Position → Flatten position
        public bool PositionTargetEnabled { get; set; } = false;
        public double PositionTargetAmount { get; set; } = 100;

        // Max Position Size → Flatten position (per symbol)
        public bool MaxPositionSizeEnabled { get; set; } = false;
        public int MaxPositionSizeDefault { get; set; } = 5; // Default if symbol not specified
        public string MaxPositionSizePerSymbol { get; set; } = "GC=2, ES=3, NQ=2"; // Format: "SYMBOL=MAX, SYMBOL=MAX"

        // ═══════════════════════════════════════════════════════════════
        // TRADE FREQUENCY RULE (Rolling window limit → Timed lockout)
        // ═══════════════════════════════════════════════════════════════
        public bool TradeFrequencyEnabled { get; set; } = false;
        public int TradeFrequencyMaxTrades { get; set; } = 5;        // Max trades allowed
        public int TradeFrequencyWindowMinutes { get; set; } = 10;   // Rolling window in minutes
        public int TradeFrequencyLockoutMinutes { get; set; } = 30;  // Lockout duration in minutes

        // ═══════════════════════════════════════════════════════════════
        // SYMBOL BLOCK LIST (Flatten position + block orders)
        // ═══════════════════════════════════════════════════════════════
        public bool SymbolBlockEnabled { get; set; } = false;
        public string SymbolBlockList { get; set; } = "";  // Comma-separated: "CL, NG, HO"

        // ═══════════════════════════════════════════════════════════════
        // GLOBAL SETTINGS
        // ═══════════════════════════════════════════════════════════════
        public bool SoundEnabled { get; set; } = true;
    }
}
