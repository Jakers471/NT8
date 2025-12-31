// RiskContext.cs
// Shared state passed to all rules for evaluation

#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Contains all current state needed for rule evaluation.
    /// Built fresh on each event, passed to all rules.
    /// </summary>
    public class RiskContext
    {
        // Account reference
        public Account Account { get; set; }

        // P&L State
        public double TotalDailyPnL { get; set; }
        public double RealizedPnL { get; set; }
        public double UnrealizedPnL { get; set; }
        public double PeakPnL { get; set; }
        public double DrawdownFromPeak => PeakPnL - TotalDailyPnL;

        // Trade History (for frequency rules)
        public List<TradeRecord> TradeHistory { get; set; } = new List<TradeRecord>();

        // Current Positions
        public Dictionary<string, PositionInfo> OpenPositions { get; set; } = new Dictionary<string, PositionInfo>();
        public int TotalOpenPositions => OpenPositions.Count;
        public int TotalOpenContracts => GetTotalContracts();

        // Pending Order (if checking before submit)
        public Order PendingOrder { get; set; }
        public string PendingOrderSymbol => PendingOrder?.Instrument?.FullName;
        public int PendingOrderSize => PendingOrder?.Quantity ?? 0;

        // Streak Tracking
        public int ConsecutiveLosses { get; set; }
        public int ConsecutiveWins { get; set; }

        // Time
        public DateTime CurrentTime => DateTime.Now;
        public TimeSpan CurrentTimeOfDay => DateTime.Now.TimeOfDay;

        // Helper: Count trades in rolling window
        public int GetTradeCountInWindow(int minutes)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            return TradeHistory?.FindAll(t => t.Time >= cutoff).Count ?? 0;
        }

        // Helper: Check if symbol is in open positions
        public bool HasPositionIn(string symbol)
        {
            return OpenPositions.ContainsKey(symbol);
        }

        private int GetTotalContracts()
        {
            int total = 0;
            foreach (var pos in OpenPositions.Values)
                total += pos.Quantity;
            return total;
        }
    }
}
