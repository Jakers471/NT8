// CopyTraderConfig.cs
// Configuration and data classes for CopyTrader

#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.CopyTrader
{
    [Serializable]
    public class CopyTraderConfig
    {
        public bool Enabled { get; set; } = false;
        public string LeaderAccount { get; set; } = "";
        public List<FollowerConfig> Followers { get; set; } = new List<FollowerConfig>();
    }

    [Serializable]
    public class FollowerConfig
    {
        public string AccountName { get; set; } = "";
        public double Multiplier { get; set; } = 1.0;  // 0.5 = half size, 2.0 = double size
        public bool Enabled { get; set; } = true;
    }

    // Tracks position state for change detection
    public class PositionSnapshot
    {
        public string Instrument { get; set; }
        public int Quantity { get; set; }
        public NinjaTrader.Cbi.MarketPosition MarketPosition { get; set; }
        public double AvgPrice { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Log entry for copy actions
    [Serializable]
    public class CopyLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; }  // "OPEN", "CLOSE", "MODIFY", "ERROR"
        public string LeaderAccount { get; set; }
        public string FollowerAccount { get; set; }
        public string Instrument { get; set; }
        public int Quantity { get; set; }
        public string Direction { get; set; }
        public string Status { get; set; }  // "Success", "Failed", "Rejected"
        public string Message { get; set; }
    }

    [Serializable]
    public class CopyLogStorage
    {
        public List<CopyLogEntry> Entries { get; set; } = new List<CopyLogEntry>();
    }
}
