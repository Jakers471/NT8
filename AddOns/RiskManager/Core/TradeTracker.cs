// TradeTracker.cs
// Fast O(1) trade state tracking - prevents duplicate actions on same trade

#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    public enum TradeState
    {
        Active,      // Trade is open, can be acted upon
        Closing,     // Close order submitted, ignore further rule violations
        Closed       // Trade is done
    }

    /// <summary>
    /// Fast trade state tracking with O(1) lookups.
    /// Prevents duplicate flatten/close actions on same trade.
    /// </summary>
    public class TradeTracker
    {
        // TradeId format: "{AccountName}:{Instrument}"
        private readonly Dictionary<string, TradeState> _tradeStates = new Dictionary<string, TradeState>();
        private readonly Dictionary<string, DateTime> _stateChangeTimes = new Dictionary<string, DateTime>();
        private readonly HashSet<string> _closingTrades = new HashSet<string>(); // Fast "is closing?" check
        private readonly object _lock = new object();

        // Stale closing state timeout (if close takes too long, allow retry)
        private const int CLOSING_TIMEOUT_SECONDS = 10;

        /// <summary>
        /// Generate trade ID from account and instrument
        /// </summary>
        public static string GetTradeId(Account account, Instrument instrument)
        {
            return $"{account.Name}:{instrument.FullName}";
        }

        public static string GetTradeId(string accountName, string instrumentName)
        {
            return $"{accountName}:{instrumentName}";
        }

        /// <summary>
        /// Check if trade can be acted upon (not already closing)
        /// O(1) lookup
        /// </summary>
        public bool CanActOn(string tradeId)
        {
            lock (_lock)
            {
                // Fast path: check closing set first
                if (_closingTrades.Contains(tradeId))
                {
                    // Check for stale closing state
                    if (_stateChangeTimes.TryGetValue(tradeId, out var changeTime))
                    {
                        if ((DateTime.Now - changeTime).TotalSeconds > CLOSING_TIMEOUT_SECONDS)
                        {
                            // Stale - allow retry
                            return true;
                        }
                    }
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Mark trade as closing - prevents further actions
        /// </summary>
        public void MarkClosing(string tradeId)
        {
            lock (_lock)
            {
                _tradeStates[tradeId] = TradeState.Closing;
                _stateChangeTimes[tradeId] = DateTime.Now;
                _closingTrades.Add(tradeId);
            }
        }

        /// <summary>
        /// Mark trade as closed - cleanup
        /// </summary>
        public void MarkClosed(string tradeId)
        {
            lock (_lock)
            {
                _tradeStates[tradeId] = TradeState.Closed;
                _stateChangeTimes[tradeId] = DateTime.Now;
                _closingTrades.Remove(tradeId);
            }
        }

        /// <summary>
        /// Mark trade as active (new position opened)
        /// </summary>
        public void MarkActive(string tradeId)
        {
            lock (_lock)
            {
                _tradeStates[tradeId] = TradeState.Active;
                _stateChangeTimes[tradeId] = DateTime.Now;
                _closingTrades.Remove(tradeId);
            }
        }

        /// <summary>
        /// Get current state of trade
        /// </summary>
        public TradeState GetState(string tradeId)
        {
            lock (_lock)
            {
                return _tradeStates.TryGetValue(tradeId, out var state) ? state : TradeState.Active;
            }
        }

        /// <summary>
        /// Check if ANY trade is currently closing for an account
        /// </summary>
        public bool HasClosingTrades(string accountName)
        {
            lock (_lock)
            {
                foreach (var tradeId in _closingTrades)
                {
                    if (tradeId.StartsWith(accountName + ":"))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Get count of closing trades for an account
        /// </summary>
        public int GetClosingCount(string accountName)
        {
            lock (_lock)
            {
                int count = 0;
                foreach (var tradeId in _closingTrades)
                {
                    if (tradeId.StartsWith(accountName + ":"))
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Clear all tracking (for reset)
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _tradeStates.Clear();
                _stateChangeTimes.Clear();
                _closingTrades.Clear();
            }
        }

        /// <summary>
        /// Remove tracking for specific account
        /// </summary>
        public void ClearAccount(string accountName)
        {
            lock (_lock)
            {
                var toRemove = new List<string>();
                foreach (var tradeId in _tradeStates.Keys)
                {
                    if (tradeId.StartsWith(accountName + ":"))
                        toRemove.Add(tradeId);
                }
                foreach (var id in toRemove)
                {
                    _tradeStates.Remove(id);
                    _stateChangeTimes.Remove(id);
                    _closingTrades.Remove(id);
                }
            }
        }
    }
}
